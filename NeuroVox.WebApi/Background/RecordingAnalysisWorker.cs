using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using NeuroVox.Domain.Entities;
using NeuroVox.Domain.Enums;
using NeuroVox.Persistence.Contexts;
using NeuroVox.WebApi.Services;

namespace NeuroVox.WebApi.Background
{
    public enum JobKind { Analysis, Training }

    public class AnalysisQueue
    {
        private readonly Channel<(JobKind Kind, Guid Id)> _channel = Channel.CreateUnbounded<(JobKind, Guid)>();
        public ValueTask EnqueueAsync(Guid id, JobKind kind = JobKind.Analysis) => _channel.Writer.WriteAsync((kind, id));
        public IAsyncEnumerable<(JobKind Kind, Guid Id)> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
    }

    // Analysis and training jobs share one queue. A job runs when an AI host is free (one job per host, so several Kaggle
    // kernels work in parallel); until then it stays Queued and is retried -- nothing fails just because Kaggle is offline.
    // ponytail: in-memory queue (jobs are re-queued from the DB on restart); move to a broker if you run several API replicas.
    public class RecordingAnalysisWorker(IServiceScopeFactory scopes, AnalysisQueue queue, AiHostPool pool, ModelStorage models, ILogger<RecordingAnalysisWorker> log) : BackgroundService
    {
        private static readonly TimeSpan WaitForHost = TimeSpan.FromSeconds(20);
        public const string WaitingText = "Bekliyor: AI sunucusu (Kaggle kernel) hazır değil veya meşgul";
        private readonly SemaphoreSlim _slots = new(4);

        protected override async Task ExecuteAsync(CancellationToken ct)
        {
            using (var scope = scopes.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<NeuroVoxDbContext>();
                var pending = await db.SpeechRecordings.IgnoreQueryFilters()
                    .Where(r => (r.AnalysisStatus == AnalysisStatus.Queued || r.AnalysisStatus == AnalysisStatus.Running) && !r.RowIsDeleted)
                    .Select(r => r.Id).ToListAsync(ct);
                foreach (var id in pending) await queue.EnqueueAsync(id);
                var runs = await db.TrainingRuns.IgnoreQueryFilters()
                    .Where(r => (r.Status == AnalysisStatus.Queued || r.Status == AnalysisStatus.Running) && !r.RowIsDeleted)
                    .Select(r => r.Id).ToListAsync(ct);
                foreach (var id in runs) await queue.EnqueueAsync(id, JobKind.Training);
            }

            await foreach (var job in queue.ReadAllAsync(ct))
            {
                await _slots.WaitAsync(ct);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var done = job.Kind == JobKind.Analysis ? await RunAsync(job.Id, ct) : await RunTrainingAsync(job.Id, ct);
                        if (!done)   // no free host: look again shortly, without blocking other jobs
                        {
                            await Task.Delay(WaitForHost, ct);
                            await queue.EnqueueAsync(job.Id, job.Kind);
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        log.LogError(ex, "{Kind} job crashed for {Id}", job.Kind, job.Id);
                        await MarkFailedAsync(job.Kind, job.Id, ex.Message);
                    }
                    finally { _slots.Release(); }
                }, CancellationToken.None);
            }
        }

        private async Task MarkFailedAsync(JobKind kind, Guid id, string error)
        {
            error = error.Length > 500 ? error[..500] : error;
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NeuroVoxDbContext>();
            if (kind == JobKind.Training)
            {
                var run = await db.TrainingRuns.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.Id == id);
                if (run is null) return;
                run.Status = AnalysisStatus.Failed; run.Error = error; run.FinishedAt = DateTime.UtcNow;
            }
            else
            {
                var rec = await db.SpeechRecordings.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.Id == id);
                if (rec is null) return;
                rec.AnalysisStatus = AnalysisStatus.Failed;
                rec.AnalysisError = error;
            }
            await db.SaveChangesAsync();
        }

        private async Task<bool> RunTrainingAsync(Guid id, CancellationToken ct)
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NeuroVoxDbContext>();
            var client = scope.ServiceProvider.GetRequiredService<ISpeechAnalysisClient>();
            var run = await db.TrainingRuns.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.Id == id && !r.RowIsDeleted, ct);
            if (run is null || run.Status is AnalysisStatus.Completed or AnalysisStatus.Failed) return true;

            // Fresh data at execution time; too little data fails fast without occupying a kernel.
            var set = await TrainingSetBuilder.BuildAsync(db, run.CustomerId);
            run.SampleCount = set.Rows;
            if (set.Rows < 10 || set.Classes < 2)
            {
                run.Status = AnalysisStatus.Failed; run.FinishedAt = DateTime.UtcNow;
                run.Error = $"Yetersiz veri: {set.Rows} ziyaret satırı, {set.Classes} sınıf (en az 10 satır ve 2 sınıf gerekir)";
                await db.SaveChangesAsync(ct);
                return true;
            }

            using var lease = await pool.TryLeaseAsync(run.CustomerId, ct);
            if (lease is null)
            {
                if (run.Error != WaitingText) { run.Status = AnalysisStatus.Queued; run.Error = WaitingText; await db.SaveChangesAsync(ct); }
                return false;
            }
            run.Status = AnalysisStatus.Running; run.StartedAt ??= DateTime.UtcNow; run.Error = null;
            await db.SaveChangesAsync(ct);

            var (zip, err) = await client.TrainAsync(lease.Host, set.Csv);
            if (zip is null)
            {
                run.Status = AnalysisStatus.Failed; run.Error = err; run.FinishedAt = DateTime.UtcNow;
            }
            else
            {
                var saved = models.Save(run.CustomerId, run.Id, zip);
                run.ArtifactPath = saved.RelativePath; run.ReportJson = saved.Report;
                run.Status = AnalysisStatus.Completed; run.Error = null; run.FinishedAt = DateTime.UtcNow;
            }
            await db.SaveChangesAsync(ct);
            return true;
        }

        /// <returns>false when no AI host was free and the job should be retried.</returns>
        private async Task<bool> RunAsync(Guid id, CancellationToken ct)
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NeuroVoxDbContext>();
            var client = scope.ServiceProvider.GetRequiredService<ISpeechAnalysisClient>();

            var rec = await db.SpeechRecordings.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.Id == id && !r.RowIsDeleted, ct);
            if (rec is null || string.IsNullOrWhiteSpace(rec.AudioFilePath)) return true;

            using var lease = await pool.TryLeaseAsync(rec.CustomerId, ct);
            if (lease is null)
            {
                if (rec.AnalysisError != WaitingText) { rec.AnalysisStatus = AnalysisStatus.Queued; rec.AnalysisError = WaitingText; await db.SaveChangesAsync(ct); }
                return false;
            }
            rec.AnalysisStatus = AnalysisStatus.Running;
            rec.AnalysisError = null;
            await db.SaveChangesAsync(ct);

            var result = await client.AnalyzeAsync(lease.Host, rec.AudioFilePath, rec.CustomerId, rec.VisitId, rec.Id);
            if (result is null)
            {
                rec.AnalysisStatus = AnalysisStatus.Failed;
                rec.AnalysisError = "AI service unavailable or rejected the request";
                await db.SaveChangesAsync(ct);
                return true;
            }

            if (!string.IsNullOrWhiteSpace(result.Transcript) && string.IsNullOrWhiteSpace(rec.TranscriptText))
            {
                rec.TranscriptText = result.Transcript;
                rec.TranscriptSource = "faster-whisper";
                rec.TranscriberVersion = result.SttModelVersion;
                rec.SpeechDurationSeconds = result.SpeechDurationSeconds;
            }

            var participantId = await db.StudyVisits.IgnoreQueryFilters()
                .Where(v => v.Id == rec.VisitId).Select(v => v.ParticipantId).FirstOrDefaultAsync(ct);

            // Re-analysis replaces earlier automatic measurements instead of duplicating them.
            var old = await db.FeatureMeasurements.IgnoreQueryFilters()
                .Where(m => m.RecordingId == rec.Id && m.Layer == MeasurementLayer.AutomaticMeasurement && !m.RowIsDeleted).ToListAsync(ct);
            old.ForEach(m => m.RowIsDeleted = true);

            var names = result.Measurements.Select(m => m.FeatureName).Distinct().ToList();
            var defs = await db.FeatureDefinitions.Where(d => names.Contains(d.FeatureName)).ToListAsync(ct);

            foreach (var m in result.Measurements)
            {
                var version = m.DefinitionVersion ?? "1.0";
                var def = defs.FirstOrDefault(d => d.FeatureName == m.FeatureName && d.AlgorithmVersion == version);
                if (def is null)
                {
                    def = new FeatureDefinition
                    {
                        Id = Guid.NewGuid(),
                        FeatureName = m.FeatureName,
                        Definition = $"Auto-documented candidate feature '{m.FeatureName}'. Definition pending research-team approval.",
                        AlgorithmVersion = version,
                        ValidationStatus = FeatureValidationStatus.EXPERIMENTAL,
                        FlaggedForResearchTeamApproval = true,
                        Layer = MeasurementLayer.AutomaticMeasurement,
                        Implementation = "neurovox-ai"
                    };
                    db.FeatureDefinitions.Add(def);
                    defs.Add(def);
                }
                db.FeatureMeasurements.Add(new FeatureMeasurement
                {
                    Id = Guid.NewGuid(),
                    CustomerId = rec.CustomerId,
                    ParticipantId = participantId,
                    VisitId = rec.VisitId,
                    RecordingId = rec.Id,
                    FeatureDefinitionId = def.Id,
                    FeatureName = m.FeatureName,
                    Layer = MeasurementLayer.AutomaticMeasurement,
                    NumericValue = m.NumericValue,
                    TextValue = m.TextValue,
                    IsCandidateAnnotation = m.IsCandidateAnnotation,
                    SoftwareVersion = m.SoftwareVersion,
                    ModelVersion = m.ModelVersion,
                    DefinitionVersion = m.DefinitionVersion
                });
            }

            rec.AnalysisStatus = AnalysisStatus.Completed;
            rec.AnalysisError = null;
            rec.AnalyzedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return true;
        }
    }
}
