using System.ComponentModel.DataAnnotations;
using BaseAuth.Application.CustomAttributes;
using BaseAuth.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NeuroVox.Application.Repositories.ResearchProtocols;
using NeuroVox.Application.Repositories.Stimuli;
using NeuroVox.Domain.Entities;

namespace NeuroVox.WebApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class ResearchProtocolsController(IResearchProtocolReadRepository read, IResearchProtocolWriteRepository write) : ControllerBase
    {
        public class CreateRequest
        {
            [Required, StringLength(200, MinimumLength = 1)] public string Name { get; set; } = string.Empty;
            [Required, StringLength(32, MinimumLength = 1)] public string Version { get; set; } = string.Empty;
            public string? Description { get; set; }
            [Range(1, 3600)] public int TargetRecordingSecondsMin { get; set; } = 180;
            [Range(1, 3600)] public int TargetRecordingSecondsMax { get; set; } = 300;
            [Required] public string CodingManualVersion { get; set; } = string.Empty;
            public string? InformationUnitSchemaVersion { get; set; }
            public bool IsBlindedAnnotationEnabled { get; set; } = true;
            [StringLength(200)] public string? EthicsCommittee { get; set; }
            [StringLength(100)] public string? EthicsApprovalNumber { get; set; }
            public DateOnly? EthicsApprovalDate { get; set; }
        }

        public class EthicsRequest
        {
            [StringLength(200)] public string? EthicsCommittee { get; set; }
            [Required, StringLength(100, MinimumLength = 1)] public string EthicsApprovalNumber { get; set; } = string.Empty;
            public DateOnly? EthicsApprovalDate { get; set; }
        }

        [HttpPost]
        [AuthorizeDefinition(Menu = "NeuroVox", Definition = "Post ResearchProtocols", ActionType = ActionType.Writing)]
        public async Task<IActionResult> Create([FromBody] CreateRequest r)
        {
            if (HttpContext.Items["customerid"] is not Guid customerId) return BadRequest(new { title = "customerid missing" });
            if (r.TargetRecordingSecondsMax < r.TargetRecordingSecondsMin) return BadRequest(new { title = "max < min" });
            if (read.GetWhere(p => p.Name == r.Name && p.Version == r.Version && !p.RowIsDeleted, tracking: false).Any())
                return Conflict(new { title = "protocol name+version exists; protocols are versioned, create a new version" });

            var e = new ResearchProtocol
            {
                Id = Guid.NewGuid(), CustomerId = customerId, Name = r.Name, Version = r.Version, Description = r.Description,
                TargetRecordingSecondsMin = r.TargetRecordingSecondsMin, TargetRecordingSecondsMax = r.TargetRecordingSecondsMax,
                CodingManualVersion = r.CodingManualVersion, InformationUnitSchemaVersion = r.InformationUnitSchemaVersion,
                IsBlindedAnnotationEnabled = r.IsBlindedAnnotationEnabled,
                EthicsCommittee = r.EthicsCommittee, EthicsApprovalNumber = r.EthicsApprovalNumber, EthicsApprovalDate = r.EthicsApprovalDate
            };
            await write.AddAsync(e);
            await write.SaveAsync();
            return Ok(new { e.Id });
        }

        // Only the approval record can change after creation; the scientific content of a protocol version stays frozen.
        [HttpPut("{id}/ethics")]
        [AuthorizeDefinition(Menu = "NeuroVox", Definition = "Post ResearchProtocols", ActionType = ActionType.Writing)]
        public async Task<IActionResult> SetEthics(Guid id, [FromBody] EthicsRequest r)
        {
            var p = read.GetWhere(x => x.Id == id && !x.RowIsDeleted).FirstOrDefault();
            if (p is null) return NotFound();
            p.EthicsCommittee = r.EthicsCommittee; p.EthicsApprovalNumber = r.EthicsApprovalNumber.Trim(); p.EthicsApprovalDate = r.EthicsApprovalDate;
            await write.SaveAsync();
            return NoContent();
        }

        [HttpGet]
        [AuthorizeDefinition(Menu = "NeuroVox", Definition = "Get ResearchProtocols", ActionType = ActionType.Reading)]
        public IActionResult GetAll() => Ok(read.GetWhere(p => !p.RowIsDeleted, tracking: false).OrderBy(p => p.Name).ThenBy(p => p.Version).ToList());
    }

    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class StimuliController(IStimulusReadRepository read, IStimulusWriteRepository write, IResearchProtocolReadRepository protocols) : ControllerBase
    {
        public class CreateRequest
        {
            [Required, StringLength(100, MinimumLength = 1)] public string StimulusId { get; set; } = string.Empty;
            [Required, StringLength(32, MinimumLength = 1)] public string Version { get; set; } = string.Empty;
            public string? Description { get; set; }
            public string? AssetPath { get; set; }
            public Guid ProtocolId { get; set; }
        }

        [HttpPost]
        [AuthorizeDefinition(Menu = "NeuroVox", Definition = "Post Stimuli", ActionType = ActionType.Writing)]
        public async Task<IActionResult> Create([FromBody] CreateRequest r)
        {
            if (HttpContext.Items["customerid"] is not Guid customerId) return BadRequest(new { title = "customerid missing" });
            if (!protocols.GetWhere(p => p.Id == r.ProtocolId && !p.RowIsDeleted, tracking: false).Any())
                return BadRequest(new { title = "protocol not found" });
            if (read.GetWhere(s => s.StimulusId == r.StimulusId && s.Version == r.Version && !s.RowIsDeleted, tracking: false).Any())
                return Conflict(new { title = "stimulus id+version exists; stimuli are versioned, create a new version" });

            var e = new Stimulus
            {
                Id = Guid.NewGuid(), CustomerId = customerId, StimulusId = r.StimulusId, Version = r.Version,
                Description = r.Description, AssetPath = r.AssetPath, ProtocolId = r.ProtocolId
            };
            await write.AddAsync(e);
            await write.SaveAsync();
            return Ok(new { e.Id });
        }

        [HttpGet]
        [AuthorizeDefinition(Menu = "NeuroVox", Definition = "Get Stimuli", ActionType = ActionType.Reading)]
        public IActionResult GetAll([FromQuery] Guid? protocolId = null)
            => Ok(read.GetWhere(s => !s.RowIsDeleted && (protocolId == null || s.ProtocolId == protocolId), tracking: false)
                .OrderBy(s => s.StimulusId).ThenBy(s => s.Version).ToList());
    }
}
