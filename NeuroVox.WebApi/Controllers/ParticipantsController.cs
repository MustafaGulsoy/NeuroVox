using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BaseAuth.Application.CustomAttributes;
using BaseAuth.Domain.Enums;
using NeuroVox.Application.Repositories.Participants;
using NeuroVox.Application.Repositories.ResearchProtocols;
using NeuroVox.Application.Repositories.StudyVisits;
using NeuroVox.Domain.Entities;
using NeuroVox.Domain.Enums;
using NeuroVox.Persistence.Contexts;
using NeuroVox.WebApi.Services;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace NeuroVox.WebApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class ParticipantsController : ControllerBase
    {
        private readonly IParticipantReadRepository _read;
        private readonly IParticipantWriteRepository _write;
        private readonly NeuroVoxDbContext _db;
        private readonly AudioStorage _storage;

        public ParticipantsController(IParticipantReadRepository read, IParticipantWriteRepository write, NeuroVoxDbContext db, AudioStorage storage)
        {
            _read = read;
            _write = write;
            _db = db;
            _storage = storage;
        }

        public class CreateRequest
        {
            [Required, StringLength(64, MinimumLength = 1)]
            public string ParticipantCode { get; set; } = string.Empty;
            public DateOnly? DateOfBirth { get; set; }
            public string? Sex { get; set; }
            public string? Notes { get; set; }
            // Consent version the participant signed; sets ConsentGivenAt = now when provided.
            public string? ConsentVersion { get; set; }
            public BaselineDiagnosis? Diagnosis { get; set; }
        }

        public class EligibilityRequest
        {
            [Required] public BaselineDiagnosis? Diagnosis { get; set; }
            [Required] public bool? WillingFollowUp6Months { get; set; }
            [Required] public bool? AdequateVisionHearing { get; set; }
            [Required] public bool? SevereMentalIllness { get; set; }
            [Required] public bool? SevereNeurologicalDeficit { get; set; }
            [Required] public bool? LanguageBarrier { get; set; }
        }

        public class ConsentRequest
        {
            public bool Given { get; set; }
            public string? ConsentVersion { get; set; }
        }

        [HttpPost]
        [AuthorizeDefinition(Menu = "NeuroVox", Definition = "Post Participants", ActionType = ActionType.Writing)]
        
        public async Task<IActionResult> Create([FromBody] CreateRequest request)
        {
            if (HttpContext.Items.TryGetValue("customerid", out var cid) && cid is Guid customerId)
            {
                if (_read.GetWhere(p => p.ParticipantCode == request.ParticipantCode, tracking: false).Any())
                    return Conflict(new { title = "participantCode already exists" });
                var entity = new Participant
                {
                    Id = Guid.NewGuid(),
                    CustomerId = customerId,
                    ParticipantCode = request.ParticipantCode,
                    DateOfBirth = request.DateOfBirth,
                    Sex = request.Sex,
                    Notes = request.Notes,
                    Diagnosis = request.Diagnosis,
                    ConsentVersion = request.ConsentVersion,
                    ConsentGivenAt = string.IsNullOrWhiteSpace(request.ConsentVersion) ? null : DateTime.UtcNow
                };
                await _write.AddAsync(entity);
                await _write.SaveAsync();
                return Ok(new { entity.Id, entity.ParticipantCode });
            }
            return BadRequest(new { title = "customerid missing" });
        }

        [HttpGet]
        [AuthorizeDefinition(Menu = "NeuroVox", Definition = "Get Participants", ActionType = ActionType.Reading)]
        
        public IActionResult GetAll([FromQuery] int skip = 0, [FromQuery] int take = 100)
            => Ok(_read.GetWhere(p => !p.RowIsDeleted, tracking: false)
                .OrderBy(p => p.ParticipantCode).Skip(Math.Max(skip, 0)).Take(Math.Clamp(take, 1, 500)).ToList());

        [HttpGet("{id}")]
        [AuthorizeDefinition(Menu = "NeuroVox", Definition = "Get Participants", ActionType = ActionType.Reading)]
        public IActionResult GetById(Guid id)
        {
            var p = _read.GetWhere(x => x.Id == id && !x.RowIsDeleted, tracking: false).FirstOrDefault();
            return p is null ? NotFound() : Ok(p);
        }

        // Records the inclusion/exclusion assessment; the result (IsEligible) gates recording uploads.
        [HttpPut("{id}/eligibility")]
        [AuthorizeDefinition(Menu = "NeuroVox", Definition = "Post Participants", ActionType = ActionType.Writing)]
        public async Task<IActionResult> SetEligibility(Guid id, [FromBody] EligibilityRequest r)
        {
            var p = _read.GetWhere(x => x.Id == id && !x.RowIsDeleted).FirstOrDefault();
            if (p is null) return NotFound();
            p.Diagnosis = r.Diagnosis; p.WillingFollowUp6Months = r.WillingFollowUp6Months; p.AdequateVisionHearing = r.AdequateVisionHearing;
            p.SevereMentalIllness = r.SevereMentalIllness; p.SevereNeurologicalDeficit = r.SevereNeurologicalDeficit; p.LanguageBarrier = r.LanguageBarrier;
            p.EligibilityAssessedAt = DateTime.UtcNow; p.EligibilityAssessedBy = User.Identity?.Name;
            await _write.SaveAsync();
            return Ok(new { eligible = p.IsEligible });
        }

        [HttpPost("{id}/consent")]
        [AuthorizeDefinition(Menu = "NeuroVox", Definition = "Post Participants", ActionType = ActionType.Writing)]
        public async Task<IActionResult> SetConsent(Guid id, [FromBody] ConsentRequest request)
        {
            var p = _read.GetWhere(x => x.Id == id && !x.RowIsDeleted).FirstOrDefault();
            if (p is null) return NotFound();
            if (request.Given)
            {
                if (string.IsNullOrWhiteSpace(request.ConsentVersion))
                    return BadRequest(new { title = "consentVersion required" });
                p.ConsentGivenAt = DateTime.UtcNow;
                p.ConsentVersion = request.ConsentVersion;
                p.ConsentWithdrawnAt = null;
            }
            else p.ConsentWithdrawnAt = DateTime.UtcNow;
            await _write.SaveAsync();
            return NoContent();
        }

        // KVKK right to erasure: removes audio files and transcripts, soft-deletes everything tied to the participant.
        [HttpDelete("{id}")]
        [AuthorizeDefinition(Menu = "NeuroVox", Definition = "Delete Participants", ActionType = ActionType.Deleting)]
        public async Task<IActionResult> Erase(Guid id)
        {
            var p = _read.GetWhere(x => x.Id == id && !x.RowIsDeleted).FirstOrDefault();
            if (p is null) return NotFound();

            var visitIds = await _db.StudyVisits.Where(v => v.ParticipantId == id).Select(v => v.Id).ToListAsync();
            var recordings = await _db.SpeechRecordings.Where(r => visitIds.Contains(r.VisitId)).ToListAsync();
            foreach (var r in recordings)
            {
                if (r.AudioFilePath is not null)
                {
                    var path = _storage.Resolve(r.AudioFilePath);
                    if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
                }
                r.AudioFilePath = null;
                r.TranscriptText = null;
                r.RowIsDeleted = true;
            }
            foreach (var m in await _db.FeatureMeasurements.Where(m => m.ParticipantId == id).ToListAsync()) m.RowIsDeleted = true;
            foreach (var a in await _db.TherapistAnnotations.Where(a => visitIds.Contains(a.VisitId)).ToListAsync()) { a.SegmentText = null; a.Note = null; a.RowIsDeleted = true; }
            foreach (var a in await _db.AceAssessments.Where(a => a.ParticipantId == id).ToListAsync()) a.RowIsDeleted = true;
            foreach (var o in await _db.ClinicalOutcomes.Where(o => o.ParticipantId == id).ToListAsync()) o.RowIsDeleted = true;
            foreach (var v in await _db.StudyVisits.Where(v => v.ParticipantId == id).ToListAsync()) v.RowIsDeleted = true;
            p.DateOfBirth = null; p.Sex = null; p.Notes = null;
            p.RowIsDeleted = true;
            await _db.SaveChangesAsync();
            return NoContent();
        }
    }

    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class StudyVisitsController : ControllerBase
    {
        private readonly IStudyVisitReadRepository _read;
        private readonly IStudyVisitWriteRepository _write;
        private readonly IParticipantReadRepository _participants;
        private readonly IResearchProtocolReadRepository _protocols;

        public StudyVisitsController(IStudyVisitReadRepository read, IStudyVisitWriteRepository write, IParticipantReadRepository participants, IResearchProtocolReadRepository protocols)
        {
            _read = read;
            _write = write;
            _participants = participants;
            _protocols = protocols;
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
                if (!_participants.GetWhere(p => p.Id == request.ParticipantId && !p.RowIsDeleted, tracking: false).Any())
                    return BadRequest(new { title = "participant not found" });
                if (!_protocols.GetWhere(p => p.Id == request.ProtocolId && !p.RowIsDeleted, tracking: false).Any())
                    return BadRequest(new { title = "protocol not found" });
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



