using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BaseAuth.Application.CustomAttributes;
using BaseAuth.Domain.Enums;
using NeuroVox.Application.Repositories.SpeechRecordings;
using NeuroVox.Application.Repositories.TherapistAnnotations;
using System.Security.Claims;
using NeuroVox.Domain.Entities;
using NeuroVox.Domain.Enums;

namespace NeuroVox.WebApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class TherapistAnnotationsController : ControllerBase
    {
        private readonly ITherapistAnnotationReadRepository _read;
        private readonly ITherapistAnnotationWriteRepository _write;
        private readonly ISpeechRecordingReadRepository _recordings;

        public TherapistAnnotationsController(ITherapistAnnotationReadRepository read, ITherapistAnnotationWriteRepository write, ISpeechRecordingReadRepository recordings)
        {
            _read = read;
            _write = write;
            _recordings = recordings;
        }

        // The rater identity always comes from the token, never from the request body.
        private Guid? CurrentUserId => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

        public class CreateRequest
        {
            public Guid RecordingId { get; set; }
            public Guid VisitId { get; set; }
            public int RaterIndex { get; set; }
            public AnnotationCategory Category { get; set; }
            public string? Severity { get; set; }
            public double? Confidence { get; set; }
            public double StartSeconds { get; set; }
            public double EndSeconds { get; set; }
            public string? SegmentText { get; set; }
            public string? Note { get; set; }
            public string AnnotationVersion { get; set; } = "1.0";
        }

        [HttpPost]
        [AuthorizeDefinition(Menu = "NeuroVox", Definition = "Post TherapistAnnotations", ActionType = ActionType.Writing)]
        
        public async Task<IActionResult> Create([FromBody] CreateRequest request)
        {
            if (HttpContext.Items.TryGetValue("customerid", out var cid) && cid is Guid customerId)
            {
                if (CurrentUserId is not Guid annotatorId) return Unauthorized();
                if (request.EndSeconds < request.StartSeconds || request.StartSeconds < 0)
                    return BadRequest(new { title = "invalid time range" });
                var rec = _recordings.GetWhere(r => r.Id == request.RecordingId && !r.RowIsDeleted, tracking: false).FirstOrDefault();
                if (rec is null) return BadRequest(new { title = "recording not found" });
                var entity = new TherapistAnnotation
                {
                    Id = Guid.NewGuid(),
                    CustomerId = customerId,
                    RecordingId = request.RecordingId,
                    VisitId = rec.VisitId,
                    AnnotatorId = annotatorId,
                    RaterIndex = request.RaterIndex,
                    Category = request.Category,
                    Severity = request.Severity,
                    Confidence = request.Confidence,
                    StartSeconds = request.StartSeconds,
                    EndSeconds = request.EndSeconds,
                    SegmentText = request.SegmentText,
                    Note = request.Note,
                    AnnotationVersion = request.AnnotationVersion,
                    SubmittedBlind = true,
                    SubmittedAt = DateTime.UtcNow
                };
                await _write.AddAsync(entity);
                await _write.SaveAsync();
                return Ok(new { entity.Id, entity.Category, entity.StartSeconds, entity.EndSeconds });
            }
            return BadRequest(new { title = "customerid missing" });
        }

        // Blind mode: a rater only ever sees their own submissions. Blinded rater
        // submissions are hidden from other raters; AI results are never in this payload.
        [HttpGet("by-recording/{recordingId}")]
        [AuthorizeDefinition(Menu = "NeuroVox", Definition = "Get TherapistAnnotations", ActionType = ActionType.Reading)]
        
        public IActionResult GetByRecording(Guid recordingId)
        {
            if (CurrentUserId is not Guid me) return Unauthorized();
            var query = _read.GetWhere(a => a.RecordingId == recordingId && a.AnnotatorId == me && !a.RowIsDeleted && a.RowIsActive, tracking: false);
            return Ok(query.OrderBy(a => a.StartSeconds)
                .Select(a => new { a.Id, a.RecordingId, a.AnnotatorId, a.RaterIndex, a.Category, a.Severity, a.Confidence, a.StartSeconds, a.EndSeconds, a.SegmentText, a.Note, a.AnnotationVersion, a.SubmittedAt })
                .ToList());
        }
    }
}



