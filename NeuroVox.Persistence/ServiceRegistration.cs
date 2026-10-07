using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
using NeuroVox.Persistence.Contexts;
using NeuroVox.Persistence.Repositories;

namespace NeuroVox.Persistence
{
    public static class ServiceRegistration
    {
        public static void AddNeuroVoxPersistenceServices(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddDbContext<NeuroVoxDbContext>(options =>
                options.UseNpgsql(configuration["NeuroVox:ConnectionString"]));

            services.AddScoped<IParticipantReadRepository, ParticipantReadRepository>();
            services.AddScoped<IParticipantWriteRepository, ParticipantWriteRepository>();
            services.AddScoped<IStudyVisitReadRepository, StudyVisitReadRepository>();
            services.AddScoped<IStudyVisitWriteRepository, StudyVisitWriteRepository>();
            services.AddScoped<ISpeechRecordingReadRepository, SpeechRecordingReadRepository>();
            services.AddScoped<ISpeechRecordingWriteRepository, SpeechRecordingWriteRepository>();
            services.AddScoped<IFeatureDefinitionReadRepository, FeatureDefinitionReadRepository>();
            services.AddScoped<IFeatureDefinitionWriteRepository, FeatureDefinitionWriteRepository>();
            services.AddScoped<IFeatureMeasurementReadRepository, FeatureMeasurementReadRepository>();
            services.AddScoped<IFeatureMeasurementWriteRepository, FeatureMeasurementWriteRepository>();
            services.AddScoped<ITherapistAnnotationReadRepository, TherapistAnnotationReadRepository>();
            services.AddScoped<ITherapistAnnotationWriteRepository, TherapistAnnotationWriteRepository>();
            services.AddScoped<IAceAssessmentReadRepository, AceAssessmentReadRepository>();
            services.AddScoped<IAceAssessmentWriteRepository, AceAssessmentWriteRepository>();
            services.AddScoped<IClinicalOutcomeReadRepository, ClinicalOutcomeReadRepository>();
            services.AddScoped<IClinicalOutcomeWriteRepository, ClinicalOutcomeWriteRepository>();
            services.AddScoped<IResearchProtocolReadRepository, ResearchProtocolReadRepository>();
            services.AddScoped<IResearchProtocolWriteRepository, ResearchProtocolWriteRepository>();
            services.AddScoped<IStimulusReadRepository, StimulusReadRepository>();
            services.AddScoped<IStimulusWriteRepository, StimulusWriteRepository>();
        }
    }
}
