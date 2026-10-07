using NeuroVox.Domain.Entities.Common;

namespace NeuroVox.Domain.Entities
{
    // ACE-III scores are stored; copyrighted test items are not embedded unless the
    // research team holds rights. Language/version forms are NOT assumed interchangeable.
    public class AceAssessment : BaseEntity
    {
        public Guid CustomerId { get; set; }
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
}
