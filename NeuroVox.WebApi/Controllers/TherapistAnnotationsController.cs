using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BaseAuth.Application.CustomAttributes;
using BaseAuth.Domain.Enums;
using NeuroVox.Application.Repositories.TherapistAnnotations;
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

        public TherapistAnnotationsController(ITherapistAnnotationReadRepository read, ITherapistAnnotationWriteRepository write)
        {
            _read = read;
            _write = write;
        }

        public class CreateRequest
        {
            public Guid RecordingId { get; set; }
            public Guid VisitId { get; set; }
            public Guid AnnotatorId { get; set; }
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
                var entity = new TherapistAnnotation
                {
                    Id = Guid.NewGuid(),
                    CustomerId = customerId,
                    RecordingId = request.RecordingId,
                    VisitId = request.VisitId,
                    AnnotatorId = request.AnnotatorId,
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
        
        public IActionResult GetByRecording(Guid recordingId, [FromQuery] Guid? annotatorId = null)
        {
            var query = _read.GetWhere(a => a.RecordingId == recordingId && !a.RowIsDeleted && a.RowIsActive, tracking: false);
            if (annotatorId.HasValue) query = query.Where(a => a.AnnotatorId == annotatorId.Value);
            return Ok(query.OrderBy(a => a.StartSeconds)
                .Select(a => new { a.Id, a.RecordingId, a.AnnotatorId, a.RaterIndex, a.Category, a.Severity, a.Confidence, a.StartSeconds, a.EndSeconds, a.SegmentText, a.Note, a.AnnotationVersion, a.SubmittedAt })
                .ToList());
        }
    }
}



