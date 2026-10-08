using BaseAuth.Application.CustomAttributes;
using BaseAuth.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NeuroVox.Domain.Enums;
using NeuroVox.Persistence.Contexts;

namespace NeuroVox.WebApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class DashboardController(NeuroVoxDbContext db) : ControllerBase
    {
        // Counts are tenant-scoped by the DbContext query filter.
        [HttpGet("summary")]
        [AuthorizeDefinition(Menu = "NeuroVox", Definition = "Get Analysis", ActionType = ActionType.Reading)]
        public async Task<IActionResult> Summary()
        {
            var recs = db.SpeechRecordings.AsNoTracking().Where(r => !r.RowIsDeleted);
            var byStatus = await recs.GroupBy(r => r.AnalysisStatus).Select(g => new { status = g.Key, count = g.Count() }).ToListAsync();
            return Ok(new
            {
                participants = await db.Participants.CountAsync(p => !p.RowIsDeleted),
                withConsent = await db.Participants.CountAsync(p => !p.RowIsDeleted && p.ConsentGivenAt != null && p.ConsentWithdrawnAt == null),
                visits = await db.StudyVisits.CountAsync(v => !v.RowIsDeleted),
                recordings = byStatus.Sum(x => x.count),
                recordingsByStatus = byStatus,
                annotations = await db.TherapistAnnotations.CountAsync(a => !a.RowIsDeleted),
                measurements = await db.FeatureMeasurements.CountAsync(m => !m.RowIsDeleted),
                recent = await recs.OrderByDescending(r => r.RowCreatedDate).Take(5)
                    .Select(r => new { r.Id, r.VisitId, r.AnalysisStatus, r.RowCreatedDate, r.RecordingDurationSeconds }).ToListAsync()
            });
        }
    }
}
