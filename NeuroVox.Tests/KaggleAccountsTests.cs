using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NeuroVox.Persistence.Contexts;
using NeuroVox.WebApi.Services;

namespace NeuroVox.Tests
{
    public class SecretProtectorTests
    {
        private static SecretProtector With(byte fill) => new(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["NeuroVox:SecretKey"] = Convert.ToBase64String(Enumerable.Repeat(fill, 32).ToArray()) }).Build());

        [Fact]
        public void RoundTrip_IsContextBound_AndTamperEvident()
        {
            var p = With(7);
            var c = p.Protect("kaggle-token", "kaggle:1");
            Assert.DoesNotContain("kaggle-token", c);
            Assert.NotEqual(c, p.Protect("kaggle-token", "kaggle:1"));   // fresh nonce
            Assert.Equal("kaggle-token", p.Unprotect(c, "kaggle:1"));
            Assert.ThrowsAny<Exception>(() => p.Unprotect(c, "kaggle:2"));          // copied onto another row
            Assert.ThrowsAny<Exception>(() => With(8).Unprotect(c, "kaggle:1"));    // wrong key
            var raw = Convert.FromBase64String(c); raw[^1] ^= 1;
            Assert.ThrowsAny<Exception>(() => p.Unprotect(Convert.ToBase64String(raw), "kaggle:1"));
        }

        [Fact]
        public void WithoutKey_IsDisabled() =>
            Assert.False(new SecretProtector(new ConfigurationBuilder().Build()).Enabled);
    }

    public class KaggleAccountsTests : IClassFixture<KaggleAccountsTests.KFactory>
    {
        private readonly KFactory _f;
        public KaggleAccountsTests(KFactory f) => _f = f;

        public class FakeKaggle : IKaggleClient
        {
            public string? LastCode;
            public bool? LastGpu;
            public Task<bool> ValidateAsync(string u, string k, CancellationToken ct) => Task.FromResult(u != "bad");
            public Task<string?> PushKernelAsync(string u, string k, string slug, string code, bool gpu, CancellationToken ct) { LastCode = code; LastGpu = gpu; return Task.FromResult<string?>(null); }
            public Task<string?> GetLogAsync(string u, string k, string slug, int max, CancellationToken ct) => Task.FromResult<string?>("kernel log");
            public Task<(string Status, string? Failure)?> GetStatusAsync(string u, string k, string slug, CancellationToken ct) => Task.FromResult<(string, string?)?>(null);
        }

        public class KFactory : ApiFlowTests.Factory
        {
            public readonly FakeKaggle Kaggle = new();
            public KFactory()
            {
                Environment.SetEnvironmentVariable("NeuroVox__SecretKey", Convert.ToBase64String(Enumerable.Repeat((byte)9, 32).ToArray()));
                Environment.SetEnvironmentVariable("NeuroVox__PublicApiUrl", "https://api.test");
            }
            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                base.ConfigureWebHost(builder);
                builder.ConfigureServices(s => { s.RemoveAll<IKaggleClient>(); s.AddSingleton<IKaggleClient>(Kaggle); });
            }
        }

        private HttpClient Client(Guid tenant, string? role)
        {
            var c = _f.CreateClient();
            c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiFlowTests.Token(tenant, role: role));
            c.DefaultRequestHeaders.Add("customerid", tenant.ToString());
            return c;
        }

        [Fact]
        public async Task AdminOnly_EncryptedAtRest_KernelRegistersAndIsRevokedOnDelete()
        {
            var tenant = Guid.NewGuid();
            var admin = Client(tenant, "NeuroVoxAdmin");
            var user = Client(tenant, "Rater");
            var body = new { username = "alice", apiKey = "plain-secret-token-1234" };

            Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsJsonAsync("/api/KaggleAccounts", body)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/KaggleAccounts")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/KaggleAccounts", new { username = "bad", apiKey = "plain-secret-token-1234" })).StatusCode);

            var add = await admin.PostAsJsonAsync("/api/KaggleAccounts", body);
            Assert.True(add.IsSuccessStatusCode, await add.Content.ReadAsStringAsync());
            var id = (await add.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("id").GetGuid();
            Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/KaggleAccounts", body)).StatusCode);

            var list = await admin.GetStringAsync("/api/KaggleAccounts");
            Assert.Contains("alice", list);
            Assert.DoesNotContain("plain-secret-token", list);
            using (var scope = _f.Services.CreateScope())
            {
                var row = await scope.ServiceProvider.GetRequiredService<NeuroVoxDbContext>().KaggleAccounts.IgnoreQueryFilters().SingleAsync(a => a.Id == id);
                Assert.DoesNotContain("plain-secret-token", row.EncryptedApiKey);
            }

            // Connect pushes a kernel that embeds the AI source and a one-time registration token.
            Assert.Equal(HttpStatusCode.Accepted, (await admin.PostAsync($"/api/KaggleAccounts/{id}/connect", null)).StatusCode);
            var code = _f.Kaggle.LastCode!;
            Assert.Contains("api.test", code);
            var token = Regex.Match(code, "REGISTER_TOKEN = \"([0-9A-F]+)\"").Groups[1].Value;
            Assert.Equal(64, token.Length);

            var anon = _f.CreateClient();
            anon.DefaultRequestHeaders.Add("customerid", tenant.ToString());
            HttpRequestMessage Reg(string t, string url) => new(HttpMethod.Post, "/api/AiHosts/register")
            { Content = JsonContent.Create(new { url }), Headers = { { "X-Register-Token", t } } };

            Assert.Equal(HttpStatusCode.BadRequest, (await anon.SendAsync(Reg(token, "https://evil.example.com"))).StatusCode);   // SSRF guard
            Assert.Equal(HttpStatusCode.Gone, (await anon.SendAsync(Reg(new string('A', 64), "https://x-y.trycloudflare.com"))).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await anon.SendAsync(Reg(token, "https://x-y.trycloudflare.com"))).StatusCode);
            Assert.Contains("\"online\"", await admin.GetStringAsync("/api/KaggleAccounts"));

            var pool = _f.Services.GetRequiredService<AiHostPool>();
            using (var lease = await pool.TryLeaseAsync(tenant, default))
            {
                Assert.Equal("https://x-y.trycloudflare.com/", lease!.Host.BaseUrl.ToString());
                Assert.True(lease.Host.Upload);
                Assert.Null(await pool.TryLeaseAsync(tenant, default));   // one job per host at a time
            }
            using (await pool.TryLeaseAsync(tenant, default) ?? throw new Exception("host should be free again")) { }

            Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/KaggleAccounts/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.Gone, (await anon.SendAsync(Reg(token, "https://x-y.trycloudflare.com"))).StatusCode);   // kernel shuts down
        }

        [Fact]
        public async Task Keeper_KeepsOneKernelUp_SpreadsByQuota_AndFallsBackToCpu()
        {
            var tenant = Guid.NewGuid();
            var admin = Client(tenant, "NeuroVoxAdmin");
            var ids = new List<Guid>();
            foreach (var u in new[] { "ann", "bob" })
            {
                var r = await admin.PostAsJsonAsync("/api/KaggleAccounts", new { username = u, apiKey = "plain-secret-token-1234" });
                ids.Add((await r.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("id").GetGuid());
            }
            async Task Mutate(Guid id, Action<Domain.Entities.KaggleAccount> f)
            {
                using var scope = _f.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<NeuroVoxDbContext>();
                var a = await db.KaggleAccounts.IgnoreQueryFilters().SingleAsync(x => x.Id == id);
                f(a); await db.SaveChangesAsync();
            }
            using var sc = _f.Services.CreateScope();
            var keeper = ActivatorUtilities.CreateInstance<NeuroVox.WebApi.Background.KaggleKeeper>(_f.Services);

            // ann has used 29h of 30h GPU, bob none: the next kernel goes to bob, on GPU.
            await Mutate(ids[0], a => { a.QuotaWeekStartUtc = keeper_week(); a.GpuSecondsThisWeek = 29 * 3600; });
            await Mutate(ids[1], a => a.QuotaWeekStartUtc = keeper_week());
            await keeper.TickAsync(default);
            Assert.True(_f.Kaggle.LastGpu);
            Assert.Contains("api.test", _f.Kaggle.LastCode!);

            // bob's kernel is starting -> no second push. Once bob is out of GPU quota, the next kernel is CPU-only.
            _f.Kaggle.LastCode = null;
            await keeper.TickAsync(default);
            Assert.Null(_f.Kaggle.LastCode);
            await Mutate(ids[1], a => { a.LastConnectAttemptUtc = DateTime.UtcNow.AddHours(-10); a.GpuSecondsThisWeek = 31 * 3600; });
            await Mutate(ids[0], a => { a.LastConnectAttemptUtc = DateTime.UtcNow.AddHours(-10); a.GpuSecondsThisWeek = 31 * 3600; });
            await keeper.TickAsync(default);
            Assert.False(_f.Kaggle.LastGpu);
        }

        private DateTime keeper_week() => _f.Services.GetRequiredService<AiHostPool>().WeekStart(DateTime.UtcNow);

        [Fact]
        public async Task Training_IsQueuedAndFailsFastWithoutData()
        {
            var tenant = Guid.NewGuid();
            var admin = Client(tenant, "NeuroVoxAdmin");
            Assert.Equal(HttpStatusCode.Accepted, (await admin.PostAsync("/api/Training", null)).StatusCode);
            for (int i = 0; i < 50; i++)
            {
                var list = await admin.GetStringAsync("/api/Training");
                if (list.Contains("Yetersiz veri")) return;
                await Task.Delay(100);
            }
            Assert.Fail("training run was not processed by the queue worker");
        }
    }
}
