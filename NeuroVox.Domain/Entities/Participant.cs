using NeuroVox.Domain.Entities.Common;
using NeuroVox.Domain.Enums;

namespace NeuroVox.Domain.Entities
{
    public class Participant : BaseEntity
    {
        public Guid CustomerId { get; set; }
        public string ParticipantCode { get; set; } = string.Empty;
        public DateOnly? DateOfBirth { get; set; }
        public string? Sex { get; set; }
        public string? Notes { get; set; }
        // KVKK: explicit consent record. Recordings must not be uploaded without it.
        public DateTime? ConsentGivenAt { get; set; }
        public string? ConsentVersion { get; set; }
        public DateTime? ConsentWithdrawnAt { get; set; }

        // Study inclusion / exclusion criteria (protocol section 2). Recordings need an eligible, assessed participant.
        public BaselineDiagnosis? Diagnosis { get; set; }          // MCI or mild Alzheimer's at entry
        public bool? WillingFollowUp6Months { get; set; }          // inclusion
        public bool? AdequateVisionHearing { get; set; }           // inclusion
        public bool? SevereMentalIllness { get; set; }             // exclusion
        public bool? SevereNeurologicalDeficit { get; set; }       // exclusion
        public bool? LanguageBarrier { get; set; }                 // exclusion (speech analysis impossible)
        public DateTime? EligibilityAssessedAt { get; set; }
        public string? EligibilityAssessedBy { get; set; }

        /// <summary>null = not assessed yet.</summary>
        public bool? IsEligible => EligibilityAssessedAt is null ? null
            : Diagnosis is not null && WillingFollowUp6Months == true && AdequateVisionHearing == true
              && SevereMentalIllness != true && SevereNeurologicalDeficit != true && LanguageBarrier != true;
    }
}
