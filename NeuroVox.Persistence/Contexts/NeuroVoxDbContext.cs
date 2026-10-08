using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NeuroVox.Application.Abstractions.Services;
using NeuroVox.Domain.Entities;

namespace NeuroVox.Persistence.Contexts
{
    public class NeuroVoxDbContext(DbContextOptions<NeuroVoxDbContext> options, IConfiguration configuration, ITenantProvider? tenant = null) : DbContext(options)
    {
        // Evaluated per query (EF parameterizes it from the context instance).
        private Guid? CurrentTenant => tenant?.CustomerId;

        public DbSet<ResearchProtocol> ResearchProtocols => Set<ResearchProtocol>();
        public DbSet<Stimulus> Stimuli => Set<Stimulus>();
        public DbSet<Participant> Participants => Set<Participant>();
        public DbSet<StudyVisit> StudyVisits => Set<StudyVisit>();
        public DbSet<SpeechRecording> SpeechRecordings => Set<SpeechRecording>();
        public DbSet<FeatureDefinition> FeatureDefinitions => Set<FeatureDefinition>();
        public DbSet<FeatureMeasurement> FeatureMeasurements => Set<FeatureMeasurement>();
        public DbSet<TherapistAnnotation> TherapistAnnotations => Set<TherapistAnnotation>();
        public DbSet<AceAssessment> AceAssessments => Set<AceAssessment>();
        public DbSet<ClinicalOutcome> ClinicalOutcomes => Set<ClinicalOutcome>();
        public DbSet<KaggleAccount> KaggleAccounts => Set<KaggleAccount>();
        public DbSet<TrainingRun> TrainingRuns => Set<TrainingRun>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.HasDefaultSchema(configuration["NeuroVox:ContextSchema"] ?? "neurovox");

            modelBuilder.Entity<Participant>().HasIndex(p => new { p.CustomerId, p.ParticipantCode }).IsUnique();
            modelBuilder.Entity<StudyVisit>().HasIndex(v => new { v.ParticipantId, v.VisitType });
            modelBuilder.Entity<SpeechRecording>().HasIndex(r => new { r.VisitId });
            modelBuilder.Entity<FeatureDefinition>().HasIndex(f => new { f.FeatureName, f.AlgorithmVersion }).IsUnique();
            modelBuilder.Entity<FeatureMeasurement>().HasIndex(m => new { m.VisitId, m.FeatureName });
            modelBuilder.Entity<TherapistAnnotation>().HasIndex(a => new { a.RecordingId, a.AnnotatorId });
            modelBuilder.Entity<AceAssessment>().HasIndex(a => new { a.ParticipantId, a.VisitId });
            modelBuilder.Entity<ClinicalOutcome>().HasIndex(o => new { o.ParticipantId });

            // Tenant isolation: every customer-owned table is filtered by the request's tenant.
            // FeatureDefinition is a global registry and intentionally unfiltered.
            modelBuilder.Entity<ResearchProtocol>().HasQueryFilter(e => CurrentTenant == null || e.CustomerId == CurrentTenant);
            modelBuilder.Entity<Stimulus>().HasQueryFilter(e => CurrentTenant == null || e.CustomerId == CurrentTenant);
            modelBuilder.Entity<Participant>().HasQueryFilter(e => CurrentTenant == null || e.CustomerId == CurrentTenant);
            modelBuilder.Entity<StudyVisit>().HasQueryFilter(e => CurrentTenant == null || e.CustomerId == CurrentTenant);
            modelBuilder.Entity<SpeechRecording>().HasQueryFilter(e => CurrentTenant == null || e.CustomerId == CurrentTenant);
            modelBuilder.Entity<FeatureMeasurement>().HasQueryFilter(e => CurrentTenant == null || e.CustomerId == CurrentTenant);
            modelBuilder.Entity<TherapistAnnotation>().HasQueryFilter(e => CurrentTenant == null || e.CustomerId == CurrentTenant);
            modelBuilder.Entity<AceAssessment>().HasQueryFilter(e => CurrentTenant == null || e.CustomerId == CurrentTenant);
            modelBuilder.Entity<ClinicalOutcome>().HasQueryFilter(e => CurrentTenant == null || e.CustomerId == CurrentTenant);
            modelBuilder.Entity<KaggleAccount>().HasQueryFilter(e => CurrentTenant == null || e.CustomerId == CurrentTenant);
            modelBuilder.Entity<KaggleAccount>().HasIndex(e => new { e.CustomerId, e.Username }).IsUnique();
            modelBuilder.Entity<KaggleAccount>().HasIndex(e => e.RegisterTokenHash);
            modelBuilder.Entity<TrainingRun>().HasQueryFilter(e => CurrentTenant == null || e.CustomerId == CurrentTenant);
        }
    }
}
