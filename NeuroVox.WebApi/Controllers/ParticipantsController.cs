using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BaseAuth.Application.CustomAttributes;
using BaseAuth.Domain.Enums;
using NeuroVox.Application.Repositories.Participants;
using NeuroVox.Application.Repositories.StudyVisits;
using NeuroVox.Domain.Entities;
using NeuroVox.Domain.Enums;

namespace NeuroVox.WebApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class ParticipantsController : ControllerBase
    {
        private readonly IParticipantReadRepository _read;
        private readonly IParticipantWriteRepository _write;

        public ParticipantsController(IParticipantReadRepository read, IParticipantWriteRepository write)
        {
            _read = read;
            _write = write;
        }

        public class CreateRequest
        {
            public string ParticipantCode { get; set; } = string.Empty;
            public DateOnly? DateOfBirth { get; set; }
            public string? Sex { get; set; }
            public string? Notes { get; set; }
        }

        [HttpPost]
        [AuthorizeDefinition(Menu = "NeuroVox", Definition = "Post Participants", ActionType = ActionType.Writing)]
        
        public async Task<IActionResult> Create([FromBody] CreateRequest request)
        {
            if (HttpContext.Items.TryGetValue("customerid", out var cid) && cid is Guid customerId)
            {
                var entity = new Participant
                {
                    Id = Guid.NewGuid(),
                    CustomerId = customerId,
                    ParticipantCode = request.ParticipantCode,
                    DateOfBirth = request.DateOfBirth,
                    Sex = request.Sex,
                    Notes = request.Notes
                };
                await _write.AddAsync(entity);
                await _write.SaveAsync();
                return Ok(new { entity.Id, entity.ParticipantCode });
            }
            return BadRequest(new { title = "customerid missing" });
        }

        [HttpGet]
        [AuthorizeDefinition(Menu = "NeuroVox", Definition = "Get Participants", ActionType = ActionType.Reading)]
        
        public IActionResult GetAll() => Ok(_read.GetWhere(p => !p.RowIsDeleted, tracking: false).ToList());
    }

    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class StudyVisitsController : ControllerBase
    {
        private readonly IStudyVisitReadRepository _read;
        private readonly IStudyVisitWriteRepository _write;

        public StudyVisitsController(IStudyVisitReadRepository read, IStudyVisitWriteRepository write)
        {
            _read = read;
            _write = write;
        }

        public class CreateRequest
        {
            public Guid ParticipantId { get; set; }
            public Guid ProtocolId { get; set; }
            public VisitType VisitType { get; set; }
            public DateOnly? ScheduledDate { get; set; }
            public DateOnly? ActualDate { get; set; }
            public string? Notes { get; set; }
        }

        [HttpPost]
        [AuthorizeDefinition(Menu = "NeuroVox", Definition = "Post Participants", ActionType = ActionType.Writing)]
        
        public async Task<IActionResult> Create([FromBody] CreateRequest request)
        {
            if (HttpContext.Items.TryGetValue("customerid", out var cid) && cid is Guid customerId)
            {
                var entity = new StudyVisit
                {
                    Id = Guid.NewGuid(),
                    CustomerId = customerId,
                    ParticipantId = request.ParticipantId,
                    ProtocolId = request.ProtocolId,
                    VisitType = request.VisitType,
                    ScheduledDate = request.ScheduledDate,
                    ActualDate = request.ActualDate,
                    Notes = request.Notes
                };
                await _write.AddAsync(entity);
                await _write.SaveAsync();
                return Ok(new { entity.Id, entity.VisitType });
            }
            return BadRequest(new { title = "customerid missing" });
        }

        [HttpGet("by-participant/{participantId}")]
        [AuthorizeDefinition(Menu = "NeuroVox", Definition = "Get Participants", ActionType = ActionType.Reading)]
        
        public IActionResult GetByParticipant(Guid participantId)
            => Ok(_read.GetWhere(v => v.ParticipantId == participantId && !v.RowIsDeleted, tracking: false).ToList());
    }
}



