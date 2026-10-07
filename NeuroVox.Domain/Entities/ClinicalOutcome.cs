using NeuroVox.Domain.Entities.Common;
using NeuroVox.Domain.Enums;

namespace NeuroVox.Domain.Entities
{
    // Independently recorded clinical outcome. Diagnosis is never inferred from speech.
    public class ClinicalOutcome : BaseEntity
    {
        public Guid CustomerId { get; set; }
        public Guid ParticipantId { get; set; }
        public Guid? VisitId { get; set; }
        public OutcomeType OutcomeType { get; set; }
        public DateOnly? DiagnosisDate { get; set; }
        public string? Evaluator { get; set; }
        public string? Notes { get; set; }
        public bool IsIndependentlyDocumented { get; set; } = true;
    }
}
