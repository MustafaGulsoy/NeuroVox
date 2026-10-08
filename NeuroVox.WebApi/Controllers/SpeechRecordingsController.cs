using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BaseAuth.Application.CustomAttributes;
using BaseAuth.Domain.Enums;
using NeuroVox.Application.Repositories.FeatureMeasurements;
using NeuroVox.Application.Repositories.Participants;
using NeuroVox.Application.Repositories.SpeechRecordings;
using NeuroVox.Application.Repositories.Stimuli;
using NeuroVox.Application.Repositories.StudyVisits;
using NeuroVox.Application.Services.Audio;
using NeuroVox.Domain.Entities;
using NeuroVox.Domain.Enums;
using NeuroVox.WebApi.Background;
using NeuroVox.WebApi.Services;

namespace NeuroVox.WebApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class SpeechRecordingsController : ControllerBase
    {
        private const long MaxUploadBytes = 200_000_000;

        private readonly ISpeechRecordingReadRepository _read;
        private readonly ISpeechRecordingWriteRepository _write;
        private readonly IFeatureMeasurementReadRepository _measurements;
        private readonly IStudyVisitReadRepository _visits;
        private readonly IParticipantReadRepository _participants;
        private readonly IStimulusReadRepository _stimuli;
        private readonly AnalysisQueue _queue;
        private readonly AudioStorage _storage;
        private readonly NeuroVox.Persistence.Contexts.NeuroVoxDbContext _db;
        private readonly IConfiguration _config;

        public SpeechRecordingsController(
            ISpeechRecordingReadRepository read,
            ISpeechRecordingWriteRepository write,
            IFeatureMeasurementReadRepository measurements,
            IStudyVisitReadRepository visits,
            IParticipantReadRepository participants,
            IStimulusReadRepository stimuli,
            AnalysisQueue queue,
            AudioStorage storage,
            NeuroVox.Persistence.Contexts.NeuroVoxDbContext db,
            IConfiguration config)
        {
            _db = db;
            _config = config;
            _read = read;
            _write = write;
            _measurements = measurements;
            _visits = visits;
            _participants = participants;
            _stimuli = stimuli;
            _queue = queue;
            _storage = storage;
        }

        public class CreateRequest
        {
            public Guid VisitId { get; set; }
            public Guid StimulusId { get; set; }
            public string StimulusVersion { get; set; } = string.Empty;
            public string InstructionVersion { get; set; } = string.Empty;
            public double RecordingDurationSeconds { get; set; }
            public string? RecordingEnvironment { get; set; }
            public string? MicrophoneDevice { get; set; }
            public AudioQuality AudioQuality { get; set; } = AudioQuality.Unknown;
        }

        // Visit/stimulus lookups go through the tenant query filter, so another tenant's ids read as "not found".
        // Consent is enforced here: no recording may exist for a participant without active consent (KVKK).
        private IActionResult? ValidateParents(Guid visitId, Guid stimulusId)
        {
            var visit = _visits.GetWhere(v => v.Id == visitId && !v.RowIsDeleted, tracking: false).FirstOrDefault();
            if (visit is null) return BadRequest(new { title = "visit not found" });
            if (!_stimuli.GetWhere(s => s.Id == stimulusId && !s.RowIsDeleted, tracking: false).Any())
                return BadRequest(new { title = "stimulus not found" });
            var participant = _participants.GetWhere(p => p.Id == visit.ParticipantId && !p.RowIsDeleted, tracking: false).FirstOrDefault();
            if (participant?.ConsentGivenAt is null || participant.ConsentWithdrawnAt is not null)
                return StatusCode(StatusCodes.Status409Conflict, new { title = "participant has no active consent" });
            // Study gates (protocol): eligibility must be assessed and met, and the protocol needs an ethics approval number.
            if (_config.GetValue("NeuroVox:RequireEligibility", true) && participant.IsEligible != true)
                return StatusCode(StatusCodes.Status409Conflict, new { title = participant.IsEligible is null
                    ? "participant eligibility has not been assessed" : "participant does not meet the inclusion/exclusion criteria" });
            if (_config.GetValue("NeuroVox:RequireEthicsApproval", false)
                && string.IsNullOrWhiteSpace(_db.ResearchProtocols.AsNoTracking().Where(p => p.Id == visit.ProtocolId).Select(p => p.EthicsApprovalNumber).FirstOrDefault()))
                return StatusCode(StatusCodes.Status409Conflict, new { title = "protocol has no ethics approval number" });
            return null;
        }

        private static object ToDto(SpeechRecording r) => new
        {
            r.Id, r.VisitId, r.StimulusId, r.StimulusVersion, r.InstructionVersion, r.RecordingDurationSeconds,
            r.SpeechDurationSeconds, r.RecordingEnvironment, r.MicrophoneDevice, r.AudioQuality,
            HasAudio = r.AudioFilePath != null, r.TranscriptText, r.TranscriptSource, r.TranscriberVersion,
            r.AnalysisStatus, r.AnalysisError, r.AnalyzedAt
        };

        [HttpPost]
        [AuthorizeDefinition(Menu = "NeuroVox", Definition = "Post SpeechRecordings", ActionType = ActionType.Writing)]
        public async Task<IActionResult> Create([FromBody] CreateRequest request)
        {
            if (HttpContext.Items["customerid"] is not Guid customerId)
                return BadRequest(new { title = "customerid missing" });
            var invalid = ValidateParents(request.VisitId, request.StimulusId);
            if (invalid is not null) return invalid;

            var entity = new SpeechRecording
            {
                Id = Guid.NewGuid(),
                CustomerId = customerId,
                VisitId = request.VisitId,
                StimulusId = request.StimulusId,
                StimulusVersion = request.StimulusVersion,
                InstructionVersion = request.InstructionVersion,
                RecordingDurationSeconds = request.RecordingDurationSeconds,
                RecordingEnvironment = request.RecordingEnvironment,
                MicrophoneDevice = request.MicrophoneDevice,
                AudioQuality = request.AudioQuality
            };
            await _write.AddAsync(entity);
            await _write.SaveAsync();
            return Ok(new { entity.Id });
        }

        [HttpGet]
        [AuthorizeDefinition(Menu = "NeuroVox", Definition = "Get SpeechRecordings", ActionType = ActionType.Reading)]
        public IActionResult List([FromQuery] Guid? visitId = null, [FromQuery] int skip = 0, [FromQuery] int take = 50)
            => Ok(_read.GetWhere(r => !r.RowIsDeleted && (visitId == null || r.VisitId == visitId), tracking: false)
                .OrderByDescending(r => r.RowCreatedDate).Skip(Math.Max(skip, 0)).Take(Math.Clamp(take, 1, 200))
                .ToList().Select(ToDto));

        [HttpGet("{id}")]
        [AuthorizeDefinition(Menu = "NeuroVox", Definition = "Get SpeechRecordings", ActionType = ActionType.Reading)]
        public IActionResult GetById(Guid id)
        {
            var rec = _read.GetWhere(r => r.Id == id && !r.RowIsDeleted, tracking: false).FirstOrDefault();
            return rec is null ? NotFound() : Ok(ToDto(rec));
        }

        [HttpPost("upload")]
        [AuthorizeDefinition(Menu = "NeuroVox", Definition = "Post SpeechRecordings", ActionType = ActionType.Writing)]
        [RequestSizeLimit(MaxUploadBytes)]
        public async Task<IActionResult> Upload([FromForm] IFormFile file, [FromForm] Guid visitId, [FromForm] Guid stimulusId, [FromForm] string stimulusVersion, [FromForm] string instructionVersion, [FromForm] double recordingDurationSeconds)
        {
            if (HttpContext.Items["customerid"] is not Guid customerId)
                return BadRequest(new { title = "customerid missing" });
            if (file is null || file.Length == 0 || file.Length > MaxUploadBytes)
                return BadRequest(new { title = "file required (max 200 MB)" });

            var invalid = ValidateParents(visitId, stimulusId);
            if (invalid is not null) return invalid;

            var header = new byte[12];
            await using (var probe = file.OpenReadStream())
                if (await probe.ReadAsync(header) < 12 || !AudioFileValidator.IsValid(file.FileName, header))
                    return BadRequest(new { title = "unsupported or corrupt audio file" });

            Directory.CreateDirectory(_storage.Root);
            var safeName = $"{Guid.NewGuid():N}{Path.GetExtension(file.FileName).ToLowerInvariant()}";
            await using (var stream = System.IO.File.Create(_storage.Resolve(safeName)))
                await file.CopyToAsync(stream);

            var rec = new SpeechRecording
            {
                Id = Guid.NewGuid(),
                CustomerId = customerId,
                VisitId = visitId,
                StimulusId = stimulusId,
                StimulusVersion = stimulusVersion,
                InstructionVersion = instructionVersion,
                RecordingDurationSeconds = recordingDurationSeconds,
                AudioFilePath = safeName
            };
            await _write.AddAsync(rec);
            await _write.SaveAsync();
            return Ok(new { rec.Id });
        }

        // Range-enabled so the labeling UI can seek.
        [HttpGet("{id}/audio")]
        [AuthorizeDefinition(Menu = "NeuroVox", Definition = "Get SpeechRecordings", ActionType = ActionType.Reading)]
        public IActionResult Audio(Guid id)
        {
            var rec = _read.GetWhere(r => r.Id == id && !r.RowIsDeleted, tracking: false).FirstOrDefault();
            if (rec?.AudioFilePath is null) return NotFound();
            var path = _storage.Resolve(rec.AudioFilePath);
            if (!System.IO.File.Exists(path)) return NotFound();
            var contentType = Path.GetExtension(path) switch
            {
                ".wav" => "audio/wav", ".mp3" => "audio/mpeg", ".m4a" => "audio/mp4",
                ".ogg" => "audio/ogg", ".flac" => "audio/flac", ".webm" => "audio/webm",
                _ => "application/octet-stream"
            };
            return PhysicalFile(path, contentType, enableRangeProcessing: true);
        }

        // Queues the AI pipeline; results are stored as CANDIDATE measurements only.
        [HttpPost("{id}/analyze")]
        [AuthorizeDefinition(Menu = "NeuroVox", Definition = "Post SpeechRecordings", ActionType = ActionType.Writing)]
        public async Task<IActionResult> Analyze(Guid id)
        {
            var rec = _read.GetWhere(r => r.Id == id && !r.RowIsDeleted).FirstOrDefault();
            if (rec is null) return NotFound();
            if (string.IsNullOrWhiteSpace(rec.AudioFilePath))
                return BadRequest(new { title = "recording has no audio" });
            if (rec.AnalysisStatus is AnalysisStatus.Queued or AnalysisStatus.Running)
                return Accepted(new { rec.Id, status = rec.AnalysisStatus });

            rec.AnalysisStatus = AnalysisStatus.Queued;
            rec.AnalysisError = null;
            await _write.SaveAsync();
            await _queue.EnqueueAsync(rec.Id);
            return Accepted(new { rec.Id, status = rec.AnalysisStatus });
        }

        [HttpGet("{id}/analysis")]
        [AuthorizeDefinition(Menu = "NeuroVox", Definition = "Get SpeechRecordings", ActionType = ActionType.Reading)]
        public IActionResult GetAnalysis(Guid id)
        {
            var rec = _read.GetWhere(r => r.Id == id && !r.RowIsDeleted, tracking: false).FirstOrDefault();
            if (rec is null) return NotFound();
            var measurements = _measurements.GetWhere(m => m.RecordingId == id && !m.RowIsDeleted && m.Layer == MeasurementLayer.AutomaticMeasurement, tracking: false)
                .Select(m => new { m.FeatureName, m.NumericValue, m.TextValue, m.IsCandidateAnnotation, m.DefinitionVersion, m.ModelVersion })
                .ToList();
            return Ok(new { rec.Id, status = rec.AnalysisStatus, rec.AnalysisError, rec.AnalyzedAt, measurements });
        }

        // KVKK erasure: removes the audio file and soft-deletes the record.
        [HttpDelete("{id}")]
        [AuthorizeDefinition(Menu = "NeuroVox", Definition = "Delete SpeechRecordings", ActionType = ActionType.Deleting)]
        public async Task<IActionResult> Delete(Guid id)
        {
            var rec = _read.GetWhere(r => r.Id == id && !r.RowIsDeleted).FirstOrDefault();
            if (rec is null) return NotFound();
            if (rec.AudioFilePath is not null)
            {
                var path = _storage.Resolve(rec.AudioFilePath);
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            }
            rec.AudioFilePath = null;
            rec.TranscriptText = null;
            rec.RowIsDeleted = true;
            await _write.SaveAsync();
            return NoContent();
        }
    }
}
