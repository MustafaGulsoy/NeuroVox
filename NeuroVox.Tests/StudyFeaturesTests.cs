using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NeuroVox.Domain.Entities;
using NeuroVox.Domain.Enums;
using NeuroVox.Persistence.Contexts;
using NeuroVox.WebApi.Services;

namespace NeuroVox.Tests
{
    // Study-plan features: eligibility + ethics gates, ACE-III in the training set, group comparison, rater agreement, visit prediction.
    public class StudyFeaturesTests : IClassFixture<StudyFeaturesTests.SFactory>
    {
        private readonly SFactory _f;
        public StudyFeaturesTests(SFactory f) => _f = f;

        public class FakeAi : ISpeechAnalysisClient
        {
            public Dictionary<string, Dictionary<string, List<double>>>? CompareInput;
            public Dictionary<string, double>? PredictInput;
            public Task<AnalysisResult?> AnalyzeAsync(AiHost h, string p, Guid c, Guid v, Guid r) => Task.FromResult<AnalysisResult?>(null);
            public Task<(byte[]? Zip, string? Error)> TrainAsync(AiHost h, string csv) => Task.FromResult<(byte[]?, string?)>((null, "n/a"));
            public Task<JsonElement?> CompareAsync(Dictionary<string, Dictionary<string, List<double>>> f)
            {
                CompareInput = f;
                return Task.FromResult<JsonElement?>(JsonDocument.Parse("{\"results\":[{\"feature\":\"ttr\",\"p\":0.01}]}").RootElement);
            }
            public Task<JsonElement?> PredictAsync(Dictionary<string, double> f)
            {
                PredictInput = f;
                return Task.FromResult<JsonElement?>(JsonDocument.Parse("{\"predicted_label\":1,\"model_estimated_risk\":0.8}").RootElement);
            }
        }

        public class SFactory : ApiFlowTests.Factory
        {
            public readonly FakeAi Ai = new();
            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                base.ConfigureWebHost(builder);
                // the study gates are ON here (the shared test factory switches them off)
                builder.UseSetting("NeuroVox:RequireEligibility", "true");
                builder.UseSetting("NeuroVox:RequireEthicsApproval", "true");
                builder.UseSetting("NeuroVox:ModelStoragePath", Path.Combine(AudioDir, "models"));
                builder.ConfigureServices(s => { s.RemoveAll<ISpeechAnalysisClient>(); s.AddSingleton<ISpeechAnalysisClient>(Ai); });
            }
        }

        private HttpClient Client(Guid tenant)
        {
            var c = _f.CreateClient();
            c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiFlowTests.Token(tenant));
            c.DefaultRequestHeaders.Add("customerid", tenant.ToString());
            return c;
        }

        private static async Task<Guid> Id(HttpResponseMessage r)
        {
            Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
            return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        }

        private static MultipartFormDataContent Upload(Guid visit, Guid stimulus) => new()
        {
            { new ByteArrayContent(System.Text.Encoding.ASCII.GetBytes("RIFF\0\0\0\0WAVEfmt 0123456789")), "file", "a.wav" },
            { new StringContent(visit.ToString()), "visitId" }, { new StringContent(stimulus.ToString()), "stimulusId" },
            { new StringContent("1"), "stimulusVersion" }, { new StringContent("1"), "instructionVersion" }, { new StringContent("5"), "recordingDurationSeconds" }
        };

        [Fact]
        public async Task Upload_RequiresMetEligibility_AndEthicsApproval()
        {
            var t = Guid.NewGuid();
            var c = Client(t);
            var protocol = await Id(await c.PostAsJsonAsync("/api/ResearchProtocols", new { name = "P", version = "1", codingManualVersion = "1" }));
            var stimulus = await Id(await c.PostAsJsonAsync("/api/Stimuli", new { stimulusId = "S", version = "1", protocolId = protocol }));
            var person = await Id(await c.PostAsJsonAsync("/api/Participants", new { participantCode = "E1", consentVersion = "v1" }));
            var visit = await Id(await c.PostAsJsonAsync("/api/StudyVisits", new { participantId = person, protocolId = protocol, visitType = 0 }));

            // 1) eligibility not assessed
            var r = await c.PostAsync("/api/SpeechRecordings/upload", Upload(visit, stimulus));
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Contains("eligibility has not been assessed", await r.Content.ReadAsStringAsync());

            // 2) assessed but excluded (severe psychiatric illness)
            var bad = new { diagnosis = 0, willingFollowUp6Months = true, adequateVisionHearing = true, severeMentalIllness = true, severeNeurologicalDeficit = false, languageBarrier = false };
            Assert.False((await (await c.PutAsJsonAsync($"/api/Participants/{person}/eligibility", bad)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("eligible").GetBoolean());
            Assert.Contains("inclusion/exclusion", await (await c.PostAsync("/api/SpeechRecordings/upload", Upload(visit, stimulus))).Content.ReadAsStringAsync());

            // 3) eligible, but the protocol has no ethics approval yet
            var good = new { diagnosis = 0, willingFollowUp6Months = true, adequateVisionHearing = true, severeMentalIllness = false, severeNeurologicalDeficit = false, languageBarrier = false };
            Assert.True((await (await c.PutAsJsonAsync($"/api/Participants/{person}/eligibility", good)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("eligible").GetBoolean());
            Assert.Contains("ethics approval", await (await c.PostAsync("/api/SpeechRecordings/upload", Upload(visit, stimulus))).Content.ReadAsStringAsync());

            // 4) approval recorded -> accepted
            Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsJsonAsync($"/api/ResearchProtocols/{protocol}/ethics",
                new { ethicsCommittee = "KEK", ethicsApprovalNumber = "2026/123", ethicsApprovalDate = "2026-01-15" })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await c.PostAsync("/api/SpeechRecordings/upload", Upload(visit, stimulus))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync($"/api/Participants/{person}/eligibility", new { diagnosis = 0 })).StatusCode);   // incomplete form
        }

        private async Task<(Guid tenant, List<Guid> visits)> SeedCohort()
        {
            var tenant = Guid.NewGuid();
            var visits = new List<Guid>();
            using var scope = _f.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NeuroVoxDbContext>();
            var protocol = Guid.NewGuid();
            // 4 converters (high ttr) + 4 stable (low ttr) + 1 excluded converter + 1 unassessed-but-fine participant without outcome
            for (int i = 0; i < 9; i++)
            {
                var converter = i < 4 || i == 8;
                var p = new Participant
                {
                    Id = Guid.NewGuid(), CustomerId = tenant, ParticipantCode = $"S{i}", ConsentGivenAt = DateTime.UtcNow,
                    Diagnosis = converter ? BaselineDiagnosis.MildAlzheimer : BaselineDiagnosis.MCI,
                    EligibilityAssessedAt = DateTime.UtcNow, WillingFollowUp6Months = true, AdequateVisionHearing = true,
                    SevereMentalIllness = i == 8, SevereNeurologicalDeficit = false, LanguageBarrier = false
                };
                var v = new StudyVisit { Id = Guid.NewGuid(), CustomerId = tenant, ParticipantId = p.Id, ProtocolId = protocol, VisitType = VisitType.Baseline };
                visits.Add(v.Id);
                db.Participants.Add(p); db.StudyVisits.Add(v);
                db.ClinicalOutcomes.Add(new ClinicalOutcome { Id = Guid.NewGuid(), CustomerId = tenant, ParticipantId = p.Id, OutcomeType = converter ? OutcomeType.ConversionToAD : OutcomeType.StableMCI });
                db.AceAssessments.Add(new AceAssessment { Id = Guid.NewGuid(), CustomerId = tenant, ParticipantId = p.Id, VisitId = v.Id, AssessmentVersion = "III", TotalScore = converter ? 70 : 90, MemoryScore = 15 });
                db.FeatureMeasurements.Add(new FeatureMeasurement
                {
                    Id = Guid.NewGuid(), CustomerId = tenant, ParticipantId = p.Id, VisitId = v.Id, RecordingId = Guid.NewGuid(), FeatureDefinitionId = Guid.NewGuid(),
                    FeatureName = "ttr", Layer = MeasurementLayer.AutomaticMeasurement, NumericValue = converter ? 0.8 + i * 0.01 : 0.5 + i * 0.01
                });
            }
            await db.SaveChangesAsync();
            return (tenant, visits);
        }

        [Fact]
        public async Task TrainingSet_HasAceColumns_ExcludesIneligible_AndKeepsLabels()
        {
            var (tenant, _) = await SeedCohort();
            using var scope = _f.Services.CreateScope();
            var set = await TrainingSetBuilder.BuildAsync(scope.ServiceProvider.GetRequiredService<NeuroVoxDbContext>(), tenant);
            var lines = set.Csv.Trim().Split('\n').Select(l => l.TrimEnd('\r')).ToList();
            Assert.Equal("participant_code,visit_id,visit_type,outcome_label,ace_memory,ace_total,ttr", lines[0]);
            Assert.Equal(8, set.Rows);                    // the 9th participant fails the exclusion criteria
            Assert.Equal(2, set.Classes);
            Assert.DoesNotContain(lines, l => l.StartsWith("S8,"));
            Assert.Contains(lines, l => l.StartsWith("S0,") && l.EndsWith(",1,15,70,0.8"));
        }

        [Fact]
        public async Task GroupComparison_BuildsParticipantLevelGroups_AndForwardsToStats()
        {
            var (tenant, _) = await SeedCohort();
            var c = Client(tenant);
            var res = await c.GetFromJsonAsync<JsonElement>("/api/Analysis/group-comparison?by=outcome");
            Assert.Equal(4, res.GetProperty("groups").GetProperty("a").GetProperty("n").GetInt32());   // converters minus the excluded one
            Assert.Equal(4, res.GetProperty("groups").GetProperty("b").GetProperty("n").GetInt32());
            Assert.Equal("ttr", res.GetProperty("results")[0].GetProperty("feature").GetString());
            var ttr = _f.Ai.CompareInput!["ttr"];
            Assert.Equal(new[] { 0.8, 0.81, 0.82, 0.83 }, ttr["a"].Select(v => Math.Round(v, 2)).ToArray());
            Assert.Equal(new[] { 0.54, 0.55, 0.56, 0.57 }, ttr["b"].Select(v => Math.Round(v, 2)).ToArray());
            Assert.Contains("ace_total", _f.Ai.CompareInput.Keys);
            Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/Analysis/group-comparison?by=zzz")).StatusCode);

            var diag = await c.GetFromJsonAsync<JsonElement>("/api/Analysis/group-comparison?by=diagnosis");
            Assert.Equal(4, diag.GetProperty("groups").GetProperty("a").GetProperty("n").GetInt32());   // mild AD at entry (the excluded one is left out)
            Assert.Equal(4, diag.GetProperty("groups").GetProperty("b").GetProperty("n").GetInt32());
            Assert.Equal("Hafif Alzheimer", diag.GetProperty("groups").GetProperty("a").GetProperty("name").GetString());

            var tiny = await Client(Guid.NewGuid()).GetFromJsonAsync<JsonElement>("/api/Analysis/group-comparison?by=outcome");
            Assert.Equal(0, tiny.GetProperty("results").GetArrayLength());   // not enough participants: no test is run
        }

        [Fact]
        public async Task RaterAgreement_ComputesKappaPerCategory()
        {
            var tenant = Guid.NewGuid();
            var rater1 = Guid.Parse("00000000-0000-0000-0000-000000000001"); var rater2 = Guid.Parse("00000000-0000-0000-0000-000000000002");
            using (var scope = _f.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<NeuroVoxDbContext>();
                void Ann(Guid rec, Guid rater, AnnotationCategory cat) => db.TherapistAnnotations.Add(new TherapistAnnotation
                    { Id = Guid.NewGuid(), CustomerId = tenant, RecordingId = rec, VisitId = Guid.NewGuid(), AnnotatorId = rater, Category = cat, SubmittedAt = DateTime.UtcNow });
                // 6 recordings, Repetition: both raters flag recs 0-2, neither flags 3-5 -> perfect agreement (kappa 1).
                // Anomia: rater1 flags 0,1 ; rater2 flags 1,2 -> imperfect.
                for (int i = 0; i < 6; i++)
                {
                    var rec = Guid.NewGuid();
                    if (i < 3) { Ann(rec, rater1, AnnotationCategory.Repetition); Ann(rec, rater2, AnnotationCategory.Repetition); }
                    else { Ann(rec, rater1, AnnotationCategory.SemanticError); Ann(rec, rater2, AnnotationCategory.SemanticError); }
                    if (i is 0 or 1) Ann(rec, rater1, AnnotationCategory.Anomia);
                    if (i is 1 or 2) Ann(rec, rater2, AnnotationCategory.Anomia);
                }
                await db.SaveChangesAsync();
            }
            var res = await Client(tenant).GetFromJsonAsync<JsonElement>("/api/Analysis/rater-agreement");
            Assert.Equal(6, res.GetProperty("recordingsWithTwoRaters").GetInt32());
            JsonElement Cat(string n) => res.GetProperty("categories").EnumerateArray().First(x => x.GetProperty("category").GetString() == n);
            Assert.Equal(1.0, Cat("Repetition").GetProperty("kappa").GetDouble());
            Assert.Equal(1.0, Cat("SemanticError").GetProperty("kappa").GetDouble());
            // Anomia: n=6, agree on recs 1 (both), 3,4,5 (neither) = 4/6; kappa = (0.667-0.5*... ) computed by hand: po=4/6, pe=(2/6*2/6)+(4/6*4/6)=20/36 -> (0.6667-0.5556)/(1-0.5556)=0.25
            var anomia = Cat("Anomia");
            Assert.Equal(0.667, anomia.GetProperty("percentAgreement").GetDouble());
            Assert.Equal(0.25, anomia.GetProperty("kappa").GetDouble());
        }

        [Fact]
        public async Task PredictVisit_UsesCurrentModelFeatureOrder()
        {
            var (tenant, visits) = await SeedCohort();
            var c = Client(tenant);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await c.GetAsync($"/api/Predictions/visit/{visits[0]}")).StatusCode);   // no model yet
            var models = _f.Services.GetRequiredService<ModelStorage>();
            Directory.CreateDirectory(Path.Combine(models.Root, "current"));
            File.WriteAllText(Path.Combine(models.Root, "current", "feature_order.json"), "[\"ttr\",\"ace_total\",\"never_measured\"]");
            var res = await c.GetFromJsonAsync<JsonElement>($"/api/Predictions/visit/{visits[0]}");
            Assert.Equal(0.8, res.GetProperty("modelEstimatedRisk").GetDouble());
            Assert.Equal(2, res.GetProperty("featuresUsed").GetInt32());
            Assert.Equal("never_measured", res.GetProperty("missing")[0].GetString());
            Assert.Equal(new[] { "ace_total", "ttr" }, _f.Ai.PredictInput!.Keys.OrderBy(k => k).ToArray());
            Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/Predictions/visit/{Guid.NewGuid()}")).StatusCode);
        }
    }
}
