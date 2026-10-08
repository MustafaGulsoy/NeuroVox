using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NeuroVox.Domain.Entities;
using NeuroVox.Persistence.Contexts;

namespace NeuroVox.WebApi.Services
{
    public record AiHost(string Key, Uri BaseUrl, string ApiKey, bool Upload);

    /// <summary>Exclusive use of one AI host (one job at a time per host); dispose to release it.</summary>
    public sealed class AiLease(AiHost host, Action release) : IDisposable
    {
        public AiHost Host => host;
        public void Dispose() => release();
    }

    // Knows which AI hosts exist for a tenant (online Kaggle kernels, else the local container), hands them out one job
    // at a time, and starts kernels (ConnectAsync) -- when to start them is KaggleKeeper's decision.
    public class AiHostPool(IServiceScopeFactory scopes, SecretProtector protector, IKaggleClient kaggle, IConfiguration config)
    {
        public static readonly TimeSpan HeartbeatFresh = TimeSpan.FromMinutes(3);
        public const string KernelSlug = "neurovox-ai";
        private readonly HashSet<string> _busy = new();

        public static bool IsOnline(KaggleAccount a) =>
            a.PublicUrl is not null && a.LastHeartbeatUtc is { } t && DateTime.UtcNow - t < HeartbeatFresh;

        public DateTime WeekStart(DateTime now)
        {
            var reset = Enum.TryParse<DayOfWeek>(config["NeuroVox:KaggleQuotaResetDay"], true, out var d) ? d : DayOfWeek.Saturday;
            return now.Date.AddDays(-(((int)now.DayOfWeek - (int)reset + 7) % 7));
        }

        public double WeeklyGpuSeconds => config.GetValue("NeuroVox:KaggleWeeklyGpuHours", 30.0) * 3600;
        public double RemainingGpuSeconds(KaggleAccount a) => Math.Max(0, WeeklyGpuSeconds - a.GpuSecondsThisWeek);

        /// <summary>GPU while the account still has quota (minus a reserve) and Kaggle has not refused a GPU session this week.</summary>
        public bool ShouldUseGpu(KaggleAccount a) =>
            RemainingGpuSeconds(a) > config.GetValue("NeuroVox:KaggleGpuReserveHours", 0.5) * 3600
            && (a.GpuExhaustedUntilUtc ?? DateTime.MinValue) < DateTime.UtcNow;

        public AiHost? LocalHost()
        {
            var url = config["NeuroVox:AiBaseUrl"] ?? "http://localhost:8000/";
            if (string.IsNullOrWhiteSpace(url) || url == "none") return null;
            return new AiHost("local", new Uri(url), config["NeuroVox:AiApiKey"] ?? "", config.GetValue<bool>("NeuroVox:AiUpload"));
        }

        /// <summary>Hosts able to serve the tenant right now. Tenants with Kaggle accounts use only those (waiting when none is online).</summary>
        public async Task<List<AiHost>> OnlineHostsAsync(Guid customerId, CancellationToken ct)
        {
            List<KaggleAccount> accounts;
            using (var scope = scopes.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<NeuroVoxDbContext>();
                accounts = await db.KaggleAccounts.IgnoreQueryFilters().AsNoTracking()
                    .Where(a => a.CustomerId == customerId && a.RowIsActive).ToListAsync(ct);
            }
            if (accounts.Count == 0 || !protector.Enabled) return LocalHost() is { } l ? [l] : [];
            return accounts.Where(IsOnline)
                .Select(a => new AiHost(a.Id.ToString(), new Uri(a.PublicUrl!.TrimEnd('/') + "/"), protector.Unprotect(a.EncryptedAiKey!, "ai:" + a.Id), true))
                .ToList();
        }

        /// <summary>A free host, or null when every host is busy/offline (the caller keeps the job queued).</summary>
        public async Task<AiLease?> TryLeaseAsync(Guid customerId, CancellationToken ct)
        {
            var hosts = await OnlineHostsAsync(customerId, ct);
            lock (_busy)
            {
                var free = hosts.Where(h => !_busy.Contains(h.Key)).OrderBy(_ => Random.Shared.Next()).FirstOrDefault();
                if (free is null) return null;
                _busy.Add(free.Key);
                return new AiLease(free, () => { lock (_busy) _busy.Remove(free.Key); });
            }
        }

        /// <summary>Pushes (and thereby starts) the AI kernel on the account. Returns null on success, else the error.</summary>
        public async Task<string?> ConnectAsync(Guid accountId, bool gpu, CancellationToken ct)
        {
            var apiUrl = config["NeuroVox:PublicApiUrl"];
            if (string.IsNullOrWhiteSpace(apiUrl)) return "NeuroVox:PublicApiUrl is not configured";
            if (!protector.Enabled) return "NeuroVox:SecretKey is not configured";

            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NeuroVoxDbContext>();
            var a = await db.KaggleAccounts.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == accountId, ct);
            if (a is null) return "account not found";

            var registerToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var aiKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            // 0 = never exit when idle (always-on); the keeper decides when kernels are needed.
            var idle = config.GetValue("NeuroVox:KaggleAlwaysOn", true) ? 0 : config.GetValue("NeuroVox:KaggleIdleMinutes", 45);
            var code = BuildKernel(apiUrl.TrimEnd('/'), a.CustomerId, registerToken, aiKey, idle);

            a.RegisterTokenHash = Hash(registerToken);   // set before the push: the kernel may call back within seconds
            a.EncryptedAiKey = protector.Protect(aiKey, "ai:" + a.Id);
            a.PublicUrl = null;
            a.LastHeartbeatUtc = null;
            a.LastConnectAttemptUtc = DateTime.UtcNow;
            a.RunningOnGpu = gpu;
            a.KernelStartedUtc = null;
            a.LastError = null;
            await db.SaveChangesAsync(ct);

            var err = await kaggle.PushKernelAsync(a.Username, protector.Unprotect(a.EncryptedApiKey, "kaggle:" + a.Id), KernelSlug, code, gpu, ct);
            if (err is not null)
            {
                a.LastError = err.Length > 300 ? err[..300] : err;
                await db.SaveChangesAsync(ct);
            }
            return err;
        }

        public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

        private static string Resource(string name)
        {
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name) ?? throw new InvalidOperationException(name);
            using var r = new StreamReader(s, Encoding.UTF8);
            return r.ReadToEnd();
        }

        private static string Pack(string text)
        {
            using var ms = new MemoryStream();
            using (var z = new ZLibStream(ms, CompressionLevel.SmallestSize, true)) z.Write(Encoding.UTF8.GetBytes(text));
            return Convert.ToBase64String(ms.ToArray());
        }

        public static string BuildKernel(string apiUrl, Guid customerId, string registerToken, string aiKey, int idleMinutes)
        {
            var src = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["app.py"] = Pack(Resource("ai/app.py")),
                ["turkish_nlp.py"] = Pack(Resource("ai/turkish_nlp.py")),
                ["stats.py"] = Pack(Resource("ai/stats.py")),
                ["llm_review.py"] = Pack(Resource("ai/llm_review.py")),
                ["train.py"] = Pack(Resource("ai/train.py"))
            });
            return Resource("ai/kernel_template.py")
                .Replace("__SRC__", JsonSerializer.Serialize(src)[1..^1])   // JSON-in-a-Python-string literal
                .Replace("__API_URL__", apiUrl)
                .Replace("__CUSTOMER_ID__", customerId.ToString())
                .Replace("__REGISTER_TOKEN__", registerToken)
                .Replace("__AI_KEY__", aiKey)
                .Replace("__IDLE_MINUTES__", idleMinutes.ToString());
        }
    }
}
