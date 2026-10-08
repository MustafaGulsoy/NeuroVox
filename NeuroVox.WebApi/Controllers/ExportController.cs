using System.Text;
using BaseAuth.Application.CustomAttributes;
using BaseAuth.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NeuroVox.Persistence.Contexts;
using NeuroVox.WebApi.Services;

namespace NeuroVox.WebApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class ExportController(NeuroVoxDbContext db) : ControllerBase
    {
        [HttpGet("training-set.csv")]
        [AuthorizeDefinition(Menu = "NeuroVox", Definition = "Get Export", ActionType = ActionType.Reading)]
        public async Task<IActionResult> TrainingSet()
        {
            if (HttpContext.Items["customerid"] is not Guid customerId) return BadRequest(new { title = "customerid missing" });
            var set = await TrainingSetBuilder.BuildAsync(db, customerId);
            return File(Encoding.UTF8.GetBytes(set.Csv), "text/csv", "training-set.csv");
        }
    }
}
