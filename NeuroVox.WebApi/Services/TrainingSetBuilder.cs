using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NeuroVox.Domain.Enums;
using NeuroVox.Persistence.Contexts;

namespace NeuroVox.WebApi.Services
{
    public record TrainingSet(string Csv, int Rows, int Classes);

    /// <summary>One study visit: mean automatic speech measurements over its recordings plus the visit's ACE-III scores (ace_*).</summary>
    public record VisitFeatures(Guid ParticipantId, string ParticipantCode, Guid VisitId, string VisitType, BaselineDiagnosis? Diagnosis,
        int? OutcomeLabel, Dictionary<string, double> Features);

    // The tenant is explicit (IgnoreQueryFilters + CustomerId) because background jobs have no request tenant.
    public static class TrainingSetBuilder
    {
        /// <summary>Consented, not-excluded participants only. Label: ConversionToAD=1, StableMCI=0; other outcomes have none (null).</summary>
        public static async Task<List<VisitFeatures>> LoadVisitsAsync(NeuroVoxDbContext db, Guid customerId, Guid? onlyVisit = null)
        {
            var outcomes = await db.ClinicalOutcomes.IgnoreQueryFilters().AsNoTracking()
                .Where(o => o.CustomerId == customerId && !o.RowIsDeleted && (o.OutcomeType == OutcomeType.ConversionToAD || o.OutcomeType == OutcomeType.StableMCI))
                .OrderByDescending(o => o.DiagnosisDate)
                .Select(o => new { o.ParticipantId, o.OutcomeType })
                .ToListAsync();
            var label = outcomes.GroupBy(o => o.ParticipantId)
                .ToDictionary(g => g.Key, g => g.First().OutcomeType == OutcomeType.ConversionToAD ? 1 : 0);

            // An assessed participant who fails the inclusion/exclusion criteria never enters analysis.
            var participants = await db.Participants.IgnoreQueryFilters().AsNoTracking()
                .Where(p => p.CustomerId == customerId && !p.RowIsDeleted && p.ConsentGivenAt != null && p.ConsentWithdrawnAt == null
                    && !(p.EligibilityAssessedAt != null && !(p.Diagnosis != null && p.WillingFollowUp6Months == true && p.AdequateVisionHearing == true
                        && p.SevereMentalIllness != true && p.SevereNeurologicalDeficit != true && p.LanguageBarrier != true)))
                .ToDictionaryAsync(p => p.Id, p => new { p.ParticipantCode, p.Diagnosis });
            var visits = await db.StudyVisits.IgnoreQueryFilters().AsNoTracking()
                .Where(v => v.CustomerId == customerId && !v.RowIsDeleted && (onlyVisit == null || v.Id == onlyVisit)).ToDictionaryAsync(v => v.Id);

            var measurements = await db.FeatureMeasurements.IgnoreQueryFilters().AsNoTracking()
                .Where(m => m.CustomerId == customerId && !m.RowIsDeleted && m.Layer == MeasurementLayer.AutomaticMeasurement && m.NumericValue != null
                    && m.FeatureName != "nlp_backend" && (onlyVisit == null || m.VisitId == onlyVisit))
                .Select(m => new { m.VisitId, m.ParticipantId, m.FeatureName, Value = m.NumericValue!.Value })
                .ToListAsync();
            var aces = (await db.AceAssessments.IgnoreQueryFilters().AsNoTracking()
                    .Where(a => a.CustomerId == customerId && !a.RowIsDeleted && (onlyVisit == null || a.VisitId == onlyVisit)).ToListAsync())
                .GroupBy(a => a.VisitId).ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.AssessmentDate).First());

            var result = new List<VisitFeatures>();
            foreach (var byVisit in measurements.GroupBy(m => m.VisitId))
            {
                var pid = byVisit.First().ParticipantId;
                if (!participants.TryGetValue(pid, out var p) || !visits.TryGetValue(byVisit.Key, out var visit)) continue;
                var f = byVisit.GroupBy(m => m.FeatureName).ToDictionary(g => g.Key, g => g.Average(m => m.Value));
                if (aces.TryGetValue(byVisit.Key, out var ace))
                {
                    void Add(string n, int? v) { if (v is { } x) f[n] = x; }
                    Add("ace_total", ace.TotalScore); Add("ace_attention", ace.AttentionScore); Add("ace_memory", ace.MemoryScore);
                    Add("ace_fluency", ace.FluencyScore); Add("ace_language", ace.LanguageScore); Add("ace_visuospatial", ace.VisuospatialScore);
                }
                result.Add(new VisitFeatures(pid, p.ParticipantCode, byVisit.Key, visit.VisitType.ToString(), p.Diagnosis,
                    label.TryGetValue(pid, out var y) ? y : null, f));
            }
            return result;
        }

        /// <summary>CSV for services/neurovox_pipeline/train.py: one row per labelled visit; features = speech means + ACE-III scores.</summary>
        public static async Task<TrainingSet> BuildAsync(NeuroVoxDbContext db, Guid customerId)
        {
            var labelled = (await LoadVisitsAsync(db, customerId)).Where(v => v.OutcomeLabel != null).ToList();
            var features = labelled.SelectMany(v => v.Features.Keys).Distinct().OrderBy(n => n).ToList();
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(',', new[] { "participant_code", "visit_id", "visit_type", "outcome_label" }.Concat(features.Select(Csv))));
            foreach (var v in labelled)
            {
                var cells = new List<string> { Csv(v.ParticipantCode), v.VisitId.ToString(), v.VisitType, v.OutcomeLabel!.Value.ToString(CultureInfo.InvariantCulture) };
                cells.AddRange(features.Select(f => v.Features.TryGetValue(f, out var x) ? x.ToString("R", CultureInfo.InvariantCulture) : ""));
                sb.AppendLine(string.Join(',', cells));
            }
            return new TrainingSet(sb.ToString(), labelled.Count, labelled.Select(v => v.OutcomeLabel).Distinct().Count());
        }

        // Quoted + formula-neutralised: this file is opened in spreadsheets.
        private static string Csv(string v)
        {
            if (v.Length > 0 && "=+-@".Contains(v[0])) v = "'" + v;
            return v.Contains(',') || v.Contains('"') || v.Contains('\n') ? $"\"{v.Replace("\"", "\"\"")}\"" : v;
        }
    }
}
