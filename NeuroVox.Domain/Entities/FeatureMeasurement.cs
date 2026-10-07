using NeuroVox.Domain.Entities.Common;
using NeuroVox.Domain.Enums;

namespace NeuroVox.Domain.Entities
{
    // A concrete measured value for one visit/recording. AI-generated candidate
    // annotations are stored here with IsCandidateAnnotation = true and must NOT be
    // treated as clinically confirmed events; the therapist layer holds final labels.
    public class FeatureMeasurement : BaseEntity
    {
        public Guid CustomerId { get; set; }
        public Guid ParticipantId { get; set; }
        public Guid VisitId { get; set; }
        public Guid RecordingId { get; set; }
        public Guid FeatureDefinitionId { get; set; }
        public string FeatureName { get; set; } = string.Empty;
        public MeasurementLayer Layer { get; set; }
        public double? NumericValue { get; set; }
        public string? TextValue { get; set; }
        public bool IsCandidateAnnotation { get; set; }
        public string? SoftwareVersion { get; set; }
        public string? ModelVersion { get; set; }
        public string? DefinitionVersion { get; set; }
    }
}
