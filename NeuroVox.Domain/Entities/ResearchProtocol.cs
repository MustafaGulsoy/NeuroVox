using NeuroVox.Domain.Entities.Common;
using NeuroVox.Domain.Enums;

namespace NeuroVox.Domain.Entities
{
    // Configurable, versioned research protocol. The research team approves stimuli,
    // recording protocol, coding manual, and all operational definitions.
    public class ResearchProtocol : BaseEntity
    {
        public Guid CustomerId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int TargetRecordingSecondsMin { get; set; } = 180;
        public int TargetRecordingSecondsMax { get; set; } = 300;
        public string CodingManualVersion { get; set; } = string.Empty;
        public string? InformationUnitSchemaVersion { get; set; }
        public bool IsBlindedAnnotationEnabled { get; set; } = true;
        // Ethics committee approval; recordings are refused for a protocol without it (NeuroVox:RequireEthicsApproval).
        public string? EthicsCommittee { get; set; }
        public string? EthicsApprovalNumber { get; set; }
        public DateOnly? EthicsApprovalDate { get; set; }
        public ReferenceStandard DefaultReferenceStandard { get; set; } = ReferenceStandard.TherapistAnnotation;
    }
}
