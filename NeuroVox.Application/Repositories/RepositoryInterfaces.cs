using NeuroVox.Application.Repositories;
using NeuroVox.Domain.Entities;

namespace NeuroVox.Application.Repositories.Participants
{
    public interface IParticipantReadRepository : IReadRepository<Participant> { }
    public interface IParticipantWriteRepository : IWriteRepository<Participant> { }
}

namespace NeuroVox.Application.Repositories.StudyVisits
{
    public interface IStudyVisitReadRepository : IReadRepository<StudyVisit> { }
    public interface IStudyVisitWriteRepository : IWriteRepository<StudyVisit> { }
}

namespace NeuroVox.Application.Repositories.SpeechRecordings
{
    public interface ISpeechRecordingReadRepository : IReadRepository<SpeechRecording> { }
    public interface ISpeechRecordingWriteRepository : IWriteRepository<SpeechRecording> { }
}

namespace NeuroVox.Application.Repositories.FeatureDefinitions
{
    public interface IFeatureDefinitionReadRepository : IReadRepository<FeatureDefinition> { }
    public interface IFeatureDefinitionWriteRepository : IWriteRepository<FeatureDefinition> { }
}

namespace NeuroVox.Application.Repositories.FeatureMeasurements
{
    public interface IFeatureMeasurementReadRepository : IReadRepository<FeatureMeasurement> { }
    public interface IFeatureMeasurementWriteRepository : IWriteRepository<FeatureMeasurement> { }
}

namespace NeuroVox.Application.Repositories.TherapistAnnotations
{
    public interface ITherapistAnnotationReadRepository : IReadRepository<TherapistAnnotation> { }
    public interface ITherapistAnnotationWriteRepository : IWriteRepository<TherapistAnnotation> { }
}

namespace NeuroVox.Application.Repositories.AceAssessments
{
    public interface IAceAssessmentReadRepository : IReadRepository<AceAssessment> { }
    public interface IAceAssessmentWriteRepository : IWriteRepository<AceAssessment> { }
}

namespace NeuroVox.Application.Repositories.ClinicalOutcomes
{
    public interface IClinicalOutcomeReadRepository : IReadRepository<ClinicalOutcome> { }
    public interface IClinicalOutcomeWriteRepository : IWriteRepository<ClinicalOutcome> { }
}

namespace NeuroVox.Application.Repositories.ResearchProtocols
{
    public interface IResearchProtocolReadRepository : IReadRepository<ResearchProtocol> { }
    public interface IResearchProtocolWriteRepository : IWriteRepository<ResearchProtocol> { }
}

namespace NeuroVox.Application.Repositories.Stimuli
{
    public interface IStimulusReadRepository : IReadRepository<Stimulus> { }
    public interface IStimulusWriteRepository : IWriteRepository<Stimulus> { }
}
