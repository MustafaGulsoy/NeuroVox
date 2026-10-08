using BaseAuth.Application.CustomAttributes;
using BaseAuth.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NeuroVox.Domain.Entities;
using NeuroVox.Domain.Enums;
using NeuroVox.Persistence.Contexts;
using NeuroVox.WebApi.Background;

namespace NeuroVox.WebApi.Controllers
{
    // Model training runs on the shared AI job queue (Kaggle kernels); the artifacts land in ModelStorage.
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class TrainingController(NeuroVoxDbContext db, AnalysisQueue queue) : ControllerBase
    {
        [HttpPost]
        [AuthorizeDefinition(Menu = "NeuroVox", Definition = "Post Training", ActionType = ActionType.Writing)]
        public async Task<IActionResult> Start()
        {
            if (HttpContext.Items["customerid"] is not Guid customerId) return BadRequest(new { title = "customerid missing" });
            if (await db.TrainingRuns.AnyAsync(r => !r.RowIsDeleted && (r.Status == AnalysisStatus.Queued || r.Status == AnalysisStatus.Running)))
                return Conflict(new { title = "a training run is already queued or running" });
            var run = new TrainingRun { Id = Guid.NewGuid(), CustomerId = customerId, RequestedBy = User.Identity?.Name };
            db.TrainingRuns.Add(run);
            await db.SaveChangesAsync();
            await queue.EnqueueAsync(run.Id, JobKind.Training);
            return Accepted(new { id = run.Id });
        }

        [HttpGet]
        [AuthorizeDefinition(Menu = "NeuroVox", Definition = "Get Training", ActionType = ActionType.Reading)]
        public async Task<IActionResult> List()
        {
            var runs = await db.TrainingRuns.AsNoTracking().Where(r => !r.RowIsDeleted)
                .OrderByDescending(r => r.RowCreatedDate).Take(20).ToListAsync();
            return Ok(runs.Select(r => new
            {
                r.Id, r.Status, r.Error, r.SampleCount, r.RequestedBy, r.RowCreatedDate, r.StartedAt, r.FinishedAt,
                Report = r.ReportJson is null ? (System.Text.Json.JsonElement?)null : System.Text.Json.JsonDocument.Parse(r.ReportJson).RootElement.Clone()
            }));
        }
    }
}
