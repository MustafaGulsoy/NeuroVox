using NeuroVox.Domain.Entities.Common;
using NeuroVox.Domain.Enums;

namespace NeuroVox.Domain.Entities
{
    // Blinded human annotation layer. In blinded mode the rater does not see AI output
    // or other raters' submissions until independent assessment is complete.
    public class TherapistAnnotation : BaseEntity
    {
        public Guid CustomerId { get; set; }
        public Guid RecordingId { get; set; }
        public Guid VisitId { get; set; }
        public Guid AnnotatorId { get; set; }
        public int RaterIndex { get; set; }
        public AnnotationCategory Category { get; set; }
        public string? Severity { get; set; }
        public double? Confidence { get; set; }
        public double StartSeconds { get; set; }
        public double EndSeconds { get; set; }
        public string? SegmentText { get; set; }
        public string? Note { get; set; }
        public string AnnotationVersion { get; set; } = "1.0";
        public bool SubmittedBlind { get; set; } = true;
        public DateTime? SubmittedAt { get; set; }
    }
}
