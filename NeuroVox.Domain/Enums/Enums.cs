namespace NeuroVox.Domain.Enums
{
    // The four measurement concepts are explicitly separated; never merge into one label.
    public enum MeasurementLayer
    {
        RawAudio = 0,
        AutomaticMeasurement = 1,
        TherapistAnnotation = 2,
        ClinicalOutcome = 3
    }

    // Every research feature must be explicitly marked.
    public enum FeatureValidationStatus
    {
        EXPERIMENTAL = 0,
        PENDING_RESEARCH_TEAM_APPROVAL = 1,
        PROTOCOL_APPROVED = 2
    }

    public enum DataValueType
    {
        Continuous = 0,
        Categorical = 1,
        Ordinal = 2,
        Count = 3,
        Ratio = 4
    }

    public enum VisitType
    {
        Baseline = 0,
        Month6 = 1,
        Month12 = 2,
        Month18 = 3,
        Month24 = 4,
        Other = 5
    }

    public enum AnnotationCategory
    {
        Anomia = 0,
        Circumlocution = 1,
        VagueExpression = 2,
        EmptyExpression = 3,
        Repetition = 4,
        PronounAmbiguity = 5,
        SemanticError = 6,
        MorphosyntacticIssue = 7,
        InformationOmission = 8,
        EventOmission = 9,
        CoherenceIssue = 10,
        CohesionIssue = 11,
        TopicDeviation = 12
    }

    public enum OutcomeType
    {
        StableMCI = 0,
        ConversionToAD = 1,
        OtherDiagnosis = 2,
        Improvement = 3,
        LossToFollowUp = 4
    }

    // Reference standard for AI vs human agreement is explicitly configured per analysis protocol.
    public enum ReferenceStandard
    {
        TherapistAnnotation = 0,
        ConsensusAnnotation = 1,
        ClinicalOutcome = 2
    }

    public enum AudioQuality
    {
        Unknown = 0,
        Poor = 1,
        Fair = 2,
        Good = 3,
        Excellent = 4
    }
}

namespace NeuroVox.Domain.Enums
{
    public enum AnalysisStatus
    {
        None = 0,
        Queued = 1,
        Running = 2,
        Completed = 3,
        Failed = 4
    }

    public enum BaselineDiagnosis
    {
        MCI = 0,
        MildAlzheimer = 1
    }
}
