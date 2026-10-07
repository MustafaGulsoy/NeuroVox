using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BaseAuth.Application.CustomAttributes;
using BaseAuth.Domain.Enums;
using NeuroVox.Application.Repositories.FeatureDefinitions;
using NeuroVox.Application.Repositories.FeatureMeasurements;
using NeuroVox.Application.Repositories.SpeechRecordings;
using NeuroVox.Application.Repositories.StudyVisits;
using NeuroVox.Domain.Entities;
using NeuroVox.Domain.Enums;
using NeuroVox.WebApi.Services;

namespace NeuroVox.WebApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class SpeechRecordingsController : ControllerBase
    {
        private readonly ISpeechRecordingReadRepository _read;
        private readonly ISpeechRecordingWriteRepository _write;
        private readonly ISpeechAnalysisClient _analysisClient;
        private readonly IFeatureMeasurementWriteRepository _measurements;
        private readonly IFeatureDefinitionReadRepository _definitionsRead;
        private readonly IFeatureDefinitionWriteRepository _definitionsWrite;
        private readonly IStudyVisitReadRepository _visitsRead;

        public SpeechRecordingsController(
            ISpeechRecordingReadRepository read,
            ISpeechRecordingWriteRepository write,
            ISpeechAnalysisClient analysisClient,
            IFeatureMeasurementWriteRepository measurements,
            IFeatureDefinitionReadRepository definitionsRead,
            IFeatureDefinitionWriteRepository definitionsWrite,
            IStudyVisitReadRepository visitsRead)
        {
            _read = read;
            _write = write;
            _analysisClient = analysisClient;
            _measurements = measurements;
            _definitionsRead = definitionsRead;
            _definitionsWrite = definitionsWrite;
            _visitsRead = visitsRead;
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
            public string? AudioFilePath { get; set; }
        }

        [HttpPost]
        [AuthorizeDefinition(Menu = "NeuroVox", Definition = "Post SpeechRecordings", ActionType = ActionType.Writing)]
        
        public async Task<IActionResult> Create([FromBody] CreateRequest request)
        {
            if (HttpContext.Items.TryGetValue("customerid", out var cid) && cid is Guid customerId)
            {
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
                    AudioQuality = request.AudioQuality,
                    AudioFilePath = request.AudioFilePath
                };
                await _write.AddAsync(entity);
                await _write.SaveAsync();
                return Ok(new { entity.Id });
            }
            return BadRequest(new { title = "customerid missing" });
        }

        [HttpGet("{id}")]
        [AuthorizeDefinition(Menu = "NeuroVox", Definition = "Get SpeechRecordings", ActionType = ActionType.Reading)]
        
        public IActionResult GetById(Guid id)
        {
            var rec = _read.GetWhere(r => r.Id == id && !r.RowIsDeleted, tracking: false).FirstOrDefault();
            return rec is null ? NotFound() : Ok(rec);
        }

        [HttpPost("upload")]
        [AuthorizeDefinition(Menu = "NeuroVox", Definition = "Post SpeechRecordings", ActionType = ActionType.Writing)]
        
        [RequestSizeLimit(500_000_000)]
        public async Task<IActionResult> Upload([FromForm] IFormFile file, [FromForm] Guid visitId, [FromForm] Guid stimulusId, [FromForm] string stimulusVersion, [FromForm] string instructionVersion, [FromForm] double recordingDurationSeconds)
        {
            if (file is null || file.Length == 0) return BadRequest(new { title = "file required" });
            if (HttpContext.Items.TryGetValue("customerid", out var cid) && cid is Guid customerId)
            {
                var uploadDir = Path.Combine(Directory.GetCurrentDirectory(), "..", "data", "audio");
                Directory.CreateDirectory(uploadDir);
                var safeName = $"{Guid.NewGuid():N}{Path.GetExtension(file.FileName)}";
                var fullPath = Path.Combine(uploadDir, safeName);
                await using (var stream = System.IO.File.Create(fullPath))
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
                    AudioFilePath = Path.GetFullPath(fullPath)
                };
                await _write.AddAsync(rec);
                await _write.SaveAsync();
                return Ok(new { rec.Id, rec.AudioFilePath });
            }
            return BadRequest(new { title = "customerid missing" });
        }

        // Triggers the AI pipeline; results are stored as CANDIDATE measurements only.
        [HttpPost("{id}/analyze")]
        [AuthorizeDefinition(Menu = "NeuroVox", Definition = "Post SpeechRecordings", ActionType = ActionType.Writing)]
        
        public async Task<IActionResult> Analyze(Guid id)
        {
            var rec = _read.GetWhere(r => r.Id == id && !r.RowIsDeleted, tracking: false).FirstOrDefault();
            if (rec is null) return NotFound();
            if (string.IsNullOrWhiteSpace(rec.AudioFilePath))
                return BadRequest(new { title = "AudioFilePath is required" });

            var result = await _analysisClient.AnalyzeAsync(rec.AudioFilePath, rec.CustomerId, rec.VisitId, rec.Id);
            if (result is null)
                return StatusCode(502, new { title = "AI service unavailable" });

            if (!string.IsNullOrWhiteSpace(result.Transcript) && string.IsNullOrWhiteSpace(rec.TranscriptText))
            {
                rec.TranscriptText = result.Transcript;
                rec.TranscriptSource = "faster-whisper";
                rec.TranscriberVersion = result.SttModelVersion;
                rec.SpeechDurationSeconds = result.SpeechDurationSeconds;
                _write.Update(rec);
                await _write.SaveAsync();
            }

            var visit = _visitsRead.GetWhere(v => v.Id == rec.VisitId, tracking: false).FirstOrDefault();
            foreach (var m in result.Measurements)
            {
                var definition = _definitionsRead.GetWhere(d => d.FeatureName == m.FeatureName, tracking: false).FirstOrDefault();
                Guid definitionId;
                if (definition is null)
                {
                    definition = new FeatureDefinition
                    {
                        Id = Guid.NewGuid(),
                        FeatureName = m.FeatureName,
                        Definition = $"Auto-documented candidate feature '{m.FeatureName}'. Definition pending research-team approval.",
                        AlgorithmVersion = m.DefinitionVersion ?? "1.0",
                        ValidationStatus = FeatureValidationStatus.EXPERIMENTAL,
                        FlaggedForResearchTeamApproval = true,
                        Layer = MeasurementLayer.AutomaticMeasurement,
                        Implementation = "neurovox-ai"
                    };
                    await _definitionsWrite.AddAsync(definition);
                    await _definitionsWrite.SaveAsync();
                }
                definitionId = definition.Id;

                await _measurements.AddAsync(new FeatureMeasurement
                {
                    Id = Guid.NewGuid(),
                    CustomerId = rec.CustomerId,
                    ParticipantId = visit?.ParticipantId ?? Guid.Empty,
                    VisitId = rec.VisitId,
                    RecordingId = rec.Id,
                    FeatureDefinitionId = definitionId,
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
            await _measurements.SaveAsync();

            return Ok(new { result.Measurements, result.Transcript, result.SttModelVersion, result.SttLanguage });
        }
    }
}



