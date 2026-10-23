using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BaseAuth.Application.CustomAttributes;
using BaseAuth.Domain.Enums;
using NeuroVox.Domain.Entities;
using NeuroVox.Domain.Enums;
using NeuroVox.Persistence.Contexts;
using System.Security.Claims;

namespace NeuroVox.WebApi.Controllers
{
    // The rater's overall aphasia-type call for a whole recording. Blind: a rater only reads and writes their own.
    // Needs the auth."Endpoints" rows from deploy/endpoint-delete-annotations.sql.
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class AphasiaRatingsController(NeuroVoxDbContext db) : ControllerBase
    {
        public class RateRequest
        {
            public AphasiaType Type { get; set; }
            public int RaterIndex { get; set; } = 1;
        }

        private Guid? CurrentUserId => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

        [HttpGet("by-recording/{recordingId}")]
        [AuthorizeDefinition(Menu = "NeuroVox", Definition = "Get AphasiaRatings", ActionType = ActionType.Reading)]
        public async Task<IActionResult> Mine(Guid recordingId)
        {
            if (CurrentUserId is not Guid me) return Unauthorized();
            var r = await db.AphasiaRatings.AsNoTracking()
                .Where(x => x.RecordingId == recordingId && x.AnnotatorId == me && !x.RowIsDeleted)
                .Select(x => new { x.Id, x.Type, x.RaterIndex, x.SubmittedAt }).FirstOrDefaultAsync();
            return Ok(r);
        }

        // Upsert: changing your mind replaces the earlier call.
        [HttpPut("by-recording/{recordingId}")]
        [AuthorizeDefinition(Menu = "NeuroVox", Definition = "Put AphasiaRatings", ActionType = ActionType.Updating)]
        public async Task<IActionResult> Rate(Guid recordingId, [FromBody] RateRequest body)
        {
            if (CurrentUserId is not Guid me) return Unauthorized();
            if (HttpContext.Items["customerid"] is not Guid customerId) return BadRequest(new { title = "customerid missing" });
            if (!Enum.IsDefined(body.Type)) return BadRequest(new { title = "invalid aphasia type" });
            var rec = await db.SpeechRecordings.AsNoTracking().Where(r => r.Id == recordingId && !r.RowIsDeleted).Select(r => new { r.VisitId }).FirstOrDefaultAsync();
            if (rec is null) return NotFound();
            var r = await db.AphasiaRatings.FirstOrDefaultAsync(x => x.RecordingId == recordingId && x.AnnotatorId == me && !x.RowIsDeleted);
            if (r is null)
                db.AphasiaRatings.Add(r = new AphasiaRating { Id = Guid.NewGuid(), CustomerId = customerId, RecordingId = recordingId, VisitId = rec.VisitId, AnnotatorId = me });
            r.Type = body.Type; r.RaterIndex = body.RaterIndex; r.SubmittedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Ok(new { r.Id, r.Type, r.RaterIndex, r.SubmittedAt });
        }
    }
}
