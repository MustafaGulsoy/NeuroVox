using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BaseAuth.Application.CustomAttributes;
using BaseAuth.Domain.Enums;
using NeuroVox.WebApi.Services;

namespace NeuroVox.WebApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class PredictionsController : ControllerBase
    {
        private readonly ISpeechAnalysisClient _client;

        public PredictionsController(ISpeechAnalysisClient client) => _client = client;

        public class PredictRequest
        {
            public Dictionary<string, double> Features { get; set; } = new();
        }

        [HttpPost("predict")]
        [AuthorizeDefinition(Menu = "NeuroVox", Definition = "Post Predictions", ActionType = ActionType.Writing)]
        
        public async Task<IActionResult> Predict([FromBody] PredictRequest request)
        {
            if (request.Features.Count is 0 or > 500 || request.Features.Values.Any(v => double.IsNaN(v) || double.IsInfinity(v)))
                return BadRequest(new { title = "features must be 1-500 finite numbers" });
            var result = await _client.PredictAsync(request.Features);
            if (result is null) return StatusCode(503, new { title = "Model unavailable or not yet trained" });
            return Ok(result);
        }
    }
}



