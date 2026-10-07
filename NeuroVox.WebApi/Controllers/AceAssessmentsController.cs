using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BaseAuth.Application.CustomAttributes;
using BaseAuth.Domain.Enums;
using NeuroVox.Application.Repositories.AceAssessments;
using NeuroVox.Application.Repositories.ClinicalOutcomes;
using NeuroVox.Domain.Entities;
using NeuroVox.Domain.Enums;

namespace NeuroVox.WebApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class AceAssessmentsController : ControllerBase
    {
        private readonly IAceAssessmentReadRepository _read;
        private readonly IAceAssessmentWriteRepository _write;

        public AceAssessmentsController(IAceAssessmentReadRepository read, IAceAssessmentWriteRepository write)
        {
            _read = read;
            _write = write;
        }

        public class CreateRequest
        {
            public Guid ParticipantId { get; set; }
            public Guid VisitId { get; set; }
            public string AssessmentVersion { get; set; } = string.Empty;
            public DateOnly AssessmentDate { get; set; }
            public string? Evaluator { get; set; }
            public int? TotalScore { get; set; }
            public int? AttentionScore { get; set; }
            public int? MemoryScore { get; set; }
            public int? FluencyScore { get; set; }
            public int? LanguageScore { get; set; }
            public int? VisuospatialScore { get; set; }
            public string? QualityStatus { get; set; }
            public string? Notes { get; set; }
        }

        [HttpPost]
        [AuthorizeDefinition(Menu = "NeuroVox", Definition = "Post AceAssessments", ActionType = ActionType.Writing)]
        
        public async Task<IActionResult> Create([FromBody] CreateRequest request)
        {
            if (HttpContext.Items.TryGetValue("customerid", out var cid) && cid is Guid customerId)
            {
                var entity = new AceAssessment
                {
                    Id = Guid.NewGuid(),
                    CustomerId = customerId,
                    ParticipantId = request.ParticipantId,
                    VisitId = request.VisitId,
                    AssessmentVersion = request.AssessmentVersion,
                    AssessmentDate = request.AssessmentDate,
                    Evaluator = request.Evaluator,
                    TotalScore = request.TotalScore,
                    AttentionScore = request.AttentionScore,
                    MemoryScore = request.MemoryScore,
                    FluencyScore = request.FluencyScore,
                    LanguageScore = request.LanguageScore,
                    VisuospatialScore = request.VisuospatialScore,
                    QualityStatus = request.QualityStatus,
                    Notes = request.Notes
                };
                await _write.AddAsync(entity);
                await _write.SaveAsync();
                return Ok(new { entity.Id });
            }
            return BadRequest(new { title = "customerid missing" });
        }

        [HttpGet("by-participant/{participantId}")]
        [AuthorizeDefinition(Menu = "NeuroVox", Definition = "Get AceAssessments", ActionType = ActionType.Reading)]
        
        public IActionResult GetByParticipant(Guid participantId)
            => Ok(_read.GetWhere(a => a.ParticipantId == participantId && !a.RowIsDeleted, tracking: false).ToList());
    }

    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class ClinicalOutcomesController : ControllerBase
    {
        private readonly IClinicalOutcomeReadRepository _read;
        private readonly IClinicalOutcomeWriteRepository _write;

        public ClinicalOutcomesController(IClinicalOutcomeReadRepository read, IClinicalOutcomeWriteRepository write)
        {
            _read = read;
            _write = write;
        }

        public class CreateRequest
        {
            public Guid ParticipantId { get; set; }
            public Guid? VisitId { get; set; }
            public OutcomeType OutcomeType { get; set; }
            public DateOnly? DiagnosisDate { get; set; }
            public string? Evaluator { get; set; }
            public string? Notes { get; set; }
        }

        [HttpPost]
        [AuthorizeDefinition(Menu = "NeuroVox", Definition = "Post AceAssessments", ActionType = ActionType.Writing)]
        
        public async Task<IActionResult> Create([FromBody] CreateRequest request)
        {
            if (HttpContext.Items.TryGetValue("customerid", out var cid) && cid is Guid customerId)
            {
                var entity = new ClinicalOutcome
                {
                    Id = Guid.NewGuid(),
                    CustomerId = customerId,
                    ParticipantId = request.ParticipantId,
                    VisitId = request.VisitId,
                    OutcomeType = request.OutcomeType,
                    DiagnosisDate = request.DiagnosisDate,
                    Evaluator = request.Evaluator,
                    Notes = request.Notes,
                    IsIndependentlyDocumented = true
                };
                await _write.AddAsync(entity);
                await _write.SaveAsync();
                return Ok(new { entity.Id });
            }
            return BadRequest(new { title = "customerid missing" });
        }

        [HttpGet("by-participant/{participantId}")]
        [AuthorizeDefinition(Menu = "NeuroVox", Definition = "Get AceAssessments", ActionType = ActionType.Reading)]
        
        public IActionResult GetByParticipant(Guid participantId)
            => Ok(_read.GetWhere(o => o.ParticipantId == participantId && !o.RowIsDeleted, tracking: false).ToList());
    }
}



