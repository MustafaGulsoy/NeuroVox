using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using BaseAuth.Application.Abstractions.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Moq;
using NeuroVox.Persistence.Contexts;

namespace NeuroVox.Tests
{
    // Boots the real pipeline (auth scheme, tenant binding, filters, controllers) on an in-memory DB.
    // Only BaseAuth's role-permission lookup is stubbed; the permission matrix itself is BaseAuth's concern.
    public class ApiFlowTests : IClassFixture<ApiFlowTests.Factory>
    {
        private const string Key = "integration-test-key-integration-test-key-123";
        private readonly Factory _f;
        public ApiFlowTests(Factory f) => _f = f;

        public class Factory : WebApplicationFactory<Program>
        {
            public readonly string AudioDir = Path.Combine(Path.GetTempPath(), "nv-test-" + Guid.NewGuid().ToString("N"));
            private readonly string _db = Guid.NewGuid().ToString();

            public Factory()
            {
                Environment.SetEnvironmentVariable("Auth__Token__SecurityKey", Key);
                Environment.SetEnvironmentVariable("Auth__Token__Issuer", "https://test");
                Environment.SetEnvironmentVariable("Auth__Token__Audience", "https://test");
                Environment.SetEnvironmentVariable("Auth__ConnectionString", "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1");
                Environment.SetEnvironmentVariable("NeuroVox__ConnectionString", "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x");
                Environment.SetEnvironmentVariable("NeuroVox__AutoMigrate", "false");
            }

            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                builder.UseEnvironment("Production");
                builder.UseSetting("NeuroVox:AudioStoragePath", AudioDir);
                builder.UseSetting("NeuroVox:RequireEligibility", "false");   // study gates have their own tests (StudyFeaturesTests)
                builder.UseSetting("NeuroVox:RequireEthicsApproval", "false");   // per-factory, env vars are process-wide
                builder.ConfigureServices(s =>
                {
                    s.RemoveAll<DbContextOptions<NeuroVoxDbContext>>();
                    s.AddDbContext<NeuroVoxDbContext>(o => o.UseInMemoryDatabase(_db));
                    var users = new Mock<IUserService>();
                    users.Setup(u => u.HasRolePermissionToEndpointAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
                    s.RemoveAll<IUserService>();
                    s.AddScoped(_ => users.Object);
                });
            }
        }

        internal static string Token(Guid tenant, Guid? user = null, string? role = null)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, "tester"),
                new Claim(ClaimTypes.NameIdentifier, (user ?? Guid.NewGuid()).ToString()),
                new Claim(ClaimTypes.UserData, tenant.ToString())
            };
            if (role is not null) claims.Add(new Claim(ClaimTypes.Role, role));
            var jwt = new JwtSecurityToken("https://test", "https://test", claims, expires: DateTime.UtcNow.AddMinutes(10),
                signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)), SecurityAlgorithms.HmacSha256));
            return new JwtSecurityTokenHandler().WriteToken(jwt);
        }

        private HttpClient Client(Guid tenant, Guid? user = null, Guid? headerTenant = null)
        {
            var c = _f.CreateClient();
            c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(tenant, user));
            c.DefaultRequestHeaders.Add("customerid", (headerTenant ?? tenant).ToString());
            return c;
        }

        private static async Task<Guid> Id(HttpResponseMessage r)
        {
            Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
            return (await r.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("id").GetGuid();
        }

        private static MultipartFormDataContent Upload(byte[] bytes, string name, Guid visit, Guid stimulus) => new()
        {
            { new ByteArrayContent(bytes), "file", name },
            { new StringContent(visit.ToString()), "visitId" },
            { new StringContent(stimulus.ToString()), "stimulusId" },
            { new StringContent("1"), "stimulusVersion" },
            { new StringContent("1"), "instructionVersion" },
            { new StringContent("5"), "recordingDurationSeconds" }
        };

        private static readonly byte[] FakeWav = Encoding.ASCII.GetBytes("RIFF\0\0\0\0WAVEfmt 0123456789");

        [Fact]
        public async Task EndToEnd_TenantIsolation_Consent_Upload_BlindAnnotations()
        {
            var tenantA = Guid.NewGuid(); var tenantB = Guid.NewGuid();
            var a = Client(tenantA);

            // Unauthenticated and tenant-mismatch requests never reach the controllers.
            var anon = _f.CreateClient();
            anon.DefaultRequestHeaders.Add("customerid", tenantA.ToString());
            Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/Participants")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Client(tenantA, headerTenant: tenantB).GetAsync("/api/Participants")).StatusCode);

            var protocol = await Id(await a.PostAsJsonAsync("/api/ResearchProtocols", new { name = "P", version = "1", codingManualVersion = "1" }));
            var stimulus = await Id(await a.PostAsJsonAsync("/api/Stimuli", new { stimulusId = "S", version = "1", protocolId = protocol }));
            var noConsent = await Id(await a.PostAsJsonAsync("/api/Participants", new { participantCode = "NC" }));
            var consented = await Id(await a.PostAsJsonAsync("/api/Participants", new { participantCode = "C1", consentVersion = "v1" }));
            Assert.Equal(HttpStatusCode.Conflict, (await a.PostAsJsonAsync("/api/Participants", new { participantCode = "C1" })).StatusCode);

            var visitNc = await Id(await a.PostAsJsonAsync("/api/StudyVisits", new { participantId = noConsent, protocolId = protocol, visitType = 0 }));
            var visit = await Id(await a.PostAsJsonAsync("/api/StudyVisits", new { participantId = consented, protocolId = protocol, visitType = 0 }));

            // Tenant B cannot see or build on tenant A's data.
            var b = Client(tenantB);
            Assert.Equal("[]", (await b.GetStringAsync("/api/Participants")).Replace(" ", "").Replace("\n", "").Replace("\r", ""));
            Assert.Equal(HttpStatusCode.BadRequest,
                (await b.PostAsync("/api/SpeechRecordings/upload", Upload(FakeWav, "a.wav", visit, stimulus))).StatusCode);

            // No active consent -> 409. Not audio -> 400. Valid -> stored.
            Assert.Equal(HttpStatusCode.Conflict,
                (await a.PostAsync("/api/SpeechRecordings/upload", Upload(FakeWav, "a.wav", visitNc, stimulus))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,
                (await a.PostAsync("/api/SpeechRecordings/upload", Upload(Encoding.ASCII.GetBytes("MZ not audio at all"), "a.wav", visit, stimulus))).StatusCode);
            var rec = await Id(await a.PostAsync("/api/SpeechRecordings/upload", Upload(FakeWav, "a.wav", visit, stimulus)));

            Assert.Equal(FakeWav, await a.GetByteArrayAsync($"/api/SpeechRecordings/{rec}/audio"));
            Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/SpeechRecordings/{rec}/audio")).StatusCode);
            Assert.Equal(HttpStatusCode.Accepted, (await a.PostAsync($"/api/SpeechRecordings/{rec}/analyze", null)).StatusCode);

            // Blind mode: rater identity comes from the token; raters never see each other's work.
            var rater1 = Guid.NewGuid(); var rater2 = Guid.NewGuid();
            var r1 = Client(tenantA, rater1); var r2 = Client(tenantA, rater2);
            var ann = new { recordingId = rec, visitId = visit, raterIndex = 1, category = 0, startSeconds = 1.0, endSeconds = 2.0 };
            Assert.True((await r1.PostAsJsonAsync("/api/TherapistAnnotations", ann)).IsSuccessStatusCode);
            Assert.Contains("\"startSeconds\"", await r1.GetStringAsync($"/api/TherapistAnnotations/by-recording/{rec}"));
            Assert.DoesNotContain("startSeconds", await r2.GetStringAsync($"/api/TherapistAnnotations/by-recording/{rec}"));

            // Erasure removes the audio file.
            Assert.Equal(HttpStatusCode.NoContent, (await a.DeleteAsync($"/api/Participants/{consented}")).StatusCode);
            Assert.Empty(Directory.GetFiles(_f.AudioDir));
        }
    }
}
