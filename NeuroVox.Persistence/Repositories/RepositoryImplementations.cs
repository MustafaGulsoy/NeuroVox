using NeuroVox.Application.Repositories.AceAssessments;
using NeuroVox.Application.Repositories.ClinicalOutcomes;
using NeuroVox.Application.Repositories.FeatureDefinitions;
using NeuroVox.Application.Repositories.FeatureMeasurements;
using NeuroVox.Application.Repositories.Participants;
using NeuroVox.Application.Repositories.ResearchProtocols;
using NeuroVox.Application.Repositories.SpeechRecordings;
using NeuroVox.Application.Repositories.Stimuli;
using NeuroVox.Application.Repositories.StudyVisits;
using NeuroVox.Application.Repositories.TherapistAnnotations;
using NeuroVox.Domain.Entities;
using NeuroVox.Persistence.Contexts;

namespace NeuroVox.Persistence.Repositories
{
    public class ParticipantReadRepository : ReadRepository<Participant>, IParticipantReadRepository { public ParticipantReadRepository(NeuroVoxDbContext context) : base(context) { } }
    public class ParticipantWriteRepository : WriteRepository<Participant>, IParticipantWriteRepository { public ParticipantWriteRepository(NeuroVoxDbContext context) : base(context) { } }

    public class StudyVisitReadRepository : ReadRepository<StudyVisit>, IStudyVisitReadRepository { public StudyVisitReadRepository(NeuroVoxDbContext context) : base(context) { } }
    public class StudyVisitWriteRepository : WriteRepository<StudyVisit>, IStudyVisitWriteRepository { public StudyVisitWriteRepository(NeuroVoxDbContext context) : base(context) { } }

    public class SpeechRecordingReadRepository : ReadRepository<SpeechRecording>, ISpeechRecordingReadRepository { public SpeechRecordingReadRepository(NeuroVoxDbContext context) : base(context) { } }
    public class SpeechRecordingWriteRepository : WriteRepository<SpeechRecording>, ISpeechRecordingWriteRepository { public SpeechRecordingWriteRepository(NeuroVoxDbContext context) : base(context) { } }

    public class FeatureDefinitionReadRepository : ReadRepository<FeatureDefinition>, IFeatureDefinitionReadRepository { public FeatureDefinitionReadRepository(NeuroVoxDbContext context) : base(context) { } }
    public class FeatureDefinitionWriteRepository : WriteRepository<FeatureDefinition>, IFeatureDefinitionWriteRepository { public FeatureDefinitionWriteRepository(NeuroVoxDbContext context) : base(context) { } }

    public class FeatureMeasurementReadRepository : ReadRepository<FeatureMeasurement>, IFeatureMeasurementReadRepository { public FeatureMeasurementReadRepository(NeuroVoxDbContext context) : base(context) { } }
    public class FeatureMeasurementWriteRepository : WriteRepository<FeatureMeasurement>, IFeatureMeasurementWriteRepository { public FeatureMeasurementWriteRepository(NeuroVoxDbContext context) : base(context) { } }

    public class TherapistAnnotationReadRepository : ReadRepository<TherapistAnnotation>, ITherapistAnnotationReadRepository { public TherapistAnnotationReadRepository(NeuroVoxDbContext context) : base(context) { } }
    public class TherapistAnnotationWriteRepository : WriteRepository<TherapistAnnotation>, ITherapistAnnotationWriteRepository { public TherapistAnnotationWriteRepository(NeuroVoxDbContext context) : base(context) { } }

    public class AceAssessmentReadRepository : ReadRepository<AceAssessment>, IAceAssessmentReadRepository { public AceAssessmentReadRepository(NeuroVoxDbContext context) : base(context) { } }
    public class AceAssessmentWriteRepository : WriteRepository<AceAssessment>, IAceAssessmentWriteRepository { public AceAssessmentWriteRepository(NeuroVoxDbContext context) : base(context) { } }

    public class ClinicalOutcomeReadRepository : ReadRepository<ClinicalOutcome>, IClinicalOutcomeReadRepository { public ClinicalOutcomeReadRepository(NeuroVoxDbContext context) : base(context) { } }
    public class ClinicalOutcomeWriteRepository : WriteRepository<ClinicalOutcome>, IClinicalOutcomeWriteRepository { public ClinicalOutcomeWriteRepository(NeuroVoxDbContext context) : base(context) { } }

    public class ResearchProtocolReadRepository : ReadRepository<ResearchProtocol>, IResearchProtocolReadRepository { public ResearchProtocolReadRepository(NeuroVoxDbContext context) : base(context) { } }
    public class ResearchProtocolWriteRepository : WriteRepository<ResearchProtocol>, IResearchProtocolWriteRepository { public ResearchProtocolWriteRepository(NeuroVoxDbContext context) : base(context) { } }

    public class StimulusReadRepository : ReadRepository<Stimulus>, IStimulusReadRepository { public StimulusReadRepository(NeuroVoxDbContext context) : base(context) { } }
    public class StimulusWriteRepository : WriteRepository<Stimulus>, IStimulusWriteRepository { public StimulusWriteRepository(NeuroVoxDbContext context) : base(context) { } }
}
