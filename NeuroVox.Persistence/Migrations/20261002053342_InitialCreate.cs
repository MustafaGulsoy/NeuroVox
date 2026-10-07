using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NeuroVox.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "neurovox");

            migrationBuilder.CreateTable(
                name: "AceAssessments",
                schema: "neurovox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParticipantId = table.Column<Guid>(type: "uuid", nullable: false),
                    VisitId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssessmentVersion = table.Column<string>(type: "text", nullable: false),
                    AssessmentDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Evaluator = table.Column<string>(type: "text", nullable: true),
                    TotalScore = table.Column<int>(type: "integer", nullable: true),
                    AttentionScore = table.Column<int>(type: "integer", nullable: true),
                    MemoryScore = table.Column<int>(type: "integer", nullable: true),
                    FluencyScore = table.Column<int>(type: "integer", nullable: true),
                    LanguageScore = table.Column<int>(type: "integer", nullable: true),
                    VisuospatialScore = table.Column<int>(type: "integer", nullable: true),
                    QualityStatus = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    RowCreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RowUpdatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RowIsActive = table.Column<bool>(type: "boolean", nullable: false),
                    RowIsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AceAssessments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ClinicalOutcomes",
                schema: "neurovox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParticipantId = table.Column<Guid>(type: "uuid", nullable: false),
                    VisitId = table.Column<Guid>(type: "uuid", nullable: true),
                    OutcomeType = table.Column<int>(type: "integer", nullable: false),
                    DiagnosisDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Evaluator = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    IsIndependentlyDocumented = table.Column<bool>(type: "boolean", nullable: false),
                    RowCreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RowUpdatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RowIsActive = table.Column<bool>(type: "boolean", nullable: false),
                    RowIsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicalOutcomes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FeatureDefinitions",
                schema: "neurovox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FeatureName = table.Column<string>(type: "text", nullable: false),
                    Definition = table.Column<string>(type: "text", nullable: false),
                    Formula = table.Column<string>(type: "text", nullable: true),
                    Language = table.Column<string>(type: "text", nullable: false),
                    NlpDependency = table.Column<string>(type: "text", nullable: true),
                    Tokenizer = table.Column<string>(type: "text", nullable: true),
                    MorphologicalAnalyzer = table.Column<string>(type: "text", nullable: true),
                    PosTagger = table.Column<string>(type: "text", nullable: true),
                    Parser = table.Column<string>(type: "text", nullable: true),
                    Implementation = table.Column<string>(type: "text", nullable: true),
                    AlgorithmVersion = table.Column<string>(type: "text", nullable: false),
                    ValidationStatus = table.Column<int>(type: "integer", nullable: false),
                    ValueType = table.Column<int>(type: "integer", nullable: false),
                    Unit = table.Column<string>(type: "text", nullable: true),
                    Layer = table.Column<int>(type: "integer", nullable: false),
                    Method = table.Column<string>(type: "text", nullable: true),
                    Citation = table.Column<string>(type: "text", nullable: true),
                    Source = table.Column<string>(type: "text", nullable: true),
                    FlaggedForResearchTeamApproval = table.Column<bool>(type: "boolean", nullable: false),
                    RowCreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RowUpdatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RowIsActive = table.Column<bool>(type: "boolean", nullable: false),
                    RowIsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeatureDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FeatureMeasurements",
                schema: "neurovox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParticipantId = table.Column<Guid>(type: "uuid", nullable: false),
                    VisitId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordingId = table.Column<Guid>(type: "uuid", nullable: false),
                    FeatureDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    FeatureName = table.Column<string>(type: "text", nullable: false),
                    Layer = table.Column<int>(type: "integer", nullable: false),
                    NumericValue = table.Column<double>(type: "double precision", nullable: true),
                    TextValue = table.Column<string>(type: "text", nullable: true),
                    IsCandidateAnnotation = table.Column<bool>(type: "boolean", nullable: false),
                    SoftwareVersion = table.Column<string>(type: "text", nullable: true),
                    ModelVersion = table.Column<string>(type: "text", nullable: true),
                    DefinitionVersion = table.Column<string>(type: "text", nullable: true),
                    RowCreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RowUpdatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RowIsActive = table.Column<bool>(type: "boolean", nullable: false),
                    RowIsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeatureMeasurements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Participants",
                schema: "neurovox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParticipantCode = table.Column<string>(type: "text", nullable: false),
                    DateOfBirth = table.Column<DateOnly>(type: "date", nullable: true),
                    Sex = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    RowCreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RowUpdatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RowIsActive = table.Column<bool>(type: "boolean", nullable: false),
                    RowIsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Participants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ResearchProtocols",
                schema: "neurovox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    TargetRecordingSecondsMin = table.Column<int>(type: "integer", nullable: false),
                    TargetRecordingSecondsMax = table.Column<int>(type: "integer", nullable: false),
                    CodingManualVersion = table.Column<string>(type: "text", nullable: false),
                    InformationUnitSchemaVersion = table.Column<string>(type: "text", nullable: true),
                    IsBlindedAnnotationEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    DefaultReferenceStandard = table.Column<int>(type: "integer", nullable: false),
                    RowCreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RowUpdatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RowIsActive = table.Column<bool>(type: "boolean", nullable: false),
                    RowIsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResearchProtocols", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SpeechRecordings",
                schema: "neurovox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    VisitId = table.Column<Guid>(type: "uuid", nullable: false),
                    StimulusId = table.Column<Guid>(type: "uuid", nullable: false),
                    StimulusVersion = table.Column<string>(type: "text", nullable: false),
                    InstructionVersion = table.Column<string>(type: "text", nullable: false),
                    RecordingDurationSeconds = table.Column<double>(type: "double precision", nullable: false),
                    SpeechDurationSeconds = table.Column<double>(type: "double precision", nullable: true),
                    RecordingEnvironment = table.Column<string>(type: "text", nullable: true),
                    MicrophoneDevice = table.Column<string>(type: "text", nullable: true),
                    AudioQuality = table.Column<int>(type: "integer", nullable: false),
                    AudioFilePath = table.Column<string>(type: "text", nullable: true),
                    TranscriptText = table.Column<string>(type: "text", nullable: true),
                    TranscriptSource = table.Column<string>(type: "text", nullable: true),
                    TranscriberVersion = table.Column<string>(type: "text", nullable: true),
                    RowCreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RowUpdatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RowIsActive = table.Column<bool>(type: "boolean", nullable: false),
                    RowIsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeechRecordings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Stimuli",
                schema: "neurovox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    StimulusId = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    AssetPath = table.Column<string>(type: "text", nullable: true),
                    ProtocolId = table.Column<Guid>(type: "uuid", nullable: false),
                    RowCreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RowUpdatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RowIsActive = table.Column<bool>(type: "boolean", nullable: false),
                    RowIsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stimuli", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StudyVisits",
                schema: "neurovox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParticipantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProtocolId = table.Column<Guid>(type: "uuid", nullable: false),
                    VisitType = table.Column<int>(type: "integer", nullable: false),
                    ScheduledDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ActualDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    RowCreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RowUpdatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RowIsActive = table.Column<bool>(type: "boolean", nullable: false),
                    RowIsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudyVisits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TherapistAnnotations",
                schema: "neurovox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordingId = table.Column<Guid>(type: "uuid", nullable: false),
                    VisitId = table.Column<Guid>(type: "uuid", nullable: false),
                    AnnotatorId = table.Column<Guid>(type: "uuid", nullable: false),
                    RaterIndex = table.Column<int>(type: "integer", nullable: false),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    Severity = table.Column<string>(type: "text", nullable: true),
                    Confidence = table.Column<double>(type: "double precision", nullable: true),
                    StartSeconds = table.Column<double>(type: "double precision", nullable: false),
                    EndSeconds = table.Column<double>(type: "double precision", nullable: false),
                    SegmentText = table.Column<string>(type: "text", nullable: true),
                    Note = table.Column<string>(type: "text", nullable: true),
                    AnnotationVersion = table.Column<string>(type: "text", nullable: false),
                    SubmittedBlind = table.Column<bool>(type: "boolean", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RowCreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RowUpdatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RowIsActive = table.Column<bool>(type: "boolean", nullable: false),
                    RowIsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TherapistAnnotations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AceAssessments_ParticipantId_VisitId",
                schema: "neurovox",
                table: "AceAssessments",
                columns: new[] { "ParticipantId", "VisitId" });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalOutcomes_ParticipantId",
                schema: "neurovox",
                table: "ClinicalOutcomes",
                column: "ParticipantId");

            migrationBuilder.CreateIndex(
                name: "IX_FeatureDefinitions_FeatureName_AlgorithmVersion",
                schema: "neurovox",
                table: "FeatureDefinitions",
                columns: new[] { "FeatureName", "AlgorithmVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FeatureMeasurements_VisitId_FeatureName",
                schema: "neurovox",
                table: "FeatureMeasurements",
                columns: new[] { "VisitId", "FeatureName" });

            migrationBuilder.CreateIndex(
                name: "IX_Participants_CustomerId_ParticipantCode",
                schema: "neurovox",
                table: "Participants",
                columns: new[] { "CustomerId", "ParticipantCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SpeechRecordings_VisitId",
                schema: "neurovox",
                table: "SpeechRecordings",
                column: "VisitId");

            migrationBuilder.CreateIndex(
                name: "IX_StudyVisits_ParticipantId_VisitType",
                schema: "neurovox",
                table: "StudyVisits",
                columns: new[] { "ParticipantId", "VisitType" });

            migrationBuilder.CreateIndex(
                name: "IX_TherapistAnnotations_RecordingId_AnnotatorId",
                schema: "neurovox",
                table: "TherapistAnnotations",
                columns: new[] { "RecordingId", "AnnotatorId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AceAssessments",
                schema: "neurovox");

            migrationBuilder.DropTable(
                name: "ClinicalOutcomes",
                schema: "neurovox");

            migrationBuilder.DropTable(
                name: "FeatureDefinitions",
                schema: "neurovox");

            migrationBuilder.DropTable(
                name: "FeatureMeasurements",
                schema: "neurovox");

            migrationBuilder.DropTable(
                name: "Participants",
                schema: "neurovox");

            migrationBuilder.DropTable(
                name: "ResearchProtocols",
                schema: "neurovox");

            migrationBuilder.DropTable(
                name: "SpeechRecordings",
                schema: "neurovox");

            migrationBuilder.DropTable(
                name: "Stimuli",
                schema: "neurovox");

            migrationBuilder.DropTable(
                name: "StudyVisits",
                schema: "neurovox");

            migrationBuilder.DropTable(
                name: "TherapistAnnotations",
                schema: "neurovox");
        }
    }
}
