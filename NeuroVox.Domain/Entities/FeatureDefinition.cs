using NeuroVox.Domain.Entities.Common;
using NeuroVox.Domain.Enums;

namespace NeuroVox.Domain.Entities
{
    // Feature registry: answers "what exactly is this parameter, how was it calculated,
    // from which audio/transcript, which software/model version, which definition version,
    // automatic or human-coded, approved by the protocol?"
    public class FeatureDefinition : BaseEntity
    {
        public string FeatureName { get; set; } = string.Empty;
        public string Definition { get; set; } = string.Empty;
        public string? Formula { get; set; }
        public string Language { get; set; } = "tr";
        public string? NlpDependency { get; set; }
        public string? Tokenizer { get; set; }
        public string? MorphologicalAnalyzer { get; set; }
        public string? PosTagger { get; set; }
        public string? Parser { get; set; }
        public string? Implementation { get; set; }
        public string AlgorithmVersion { get; set; } = "1.0";
        public FeatureValidationStatus ValidationStatus { get; set; } = FeatureValidationStatus.EXPERIMENTAL;
        public DataValueType ValueType { get; set; } = DataValueType.Continuous;
        public string? Unit { get; set; }
        public MeasurementLayer Layer { get; set; } = MeasurementLayer.AutomaticMeasurement;
        public string? Method { get; set; }
        public string? Citation { get; set; }
        public string? Source { get; set; }
        public bool FlaggedForResearchTeamApproval { get; set; }
    }
}
