using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace NeuroVox.WebApi.Services
{
    public interface IKaggleClient
    {
        /// <summary>True when Kaggle accepts the credentials.</summary>
        Task<bool> ValidateAsync(string username, string apiKey, CancellationToken ct);
        /// <summary>Creates/updates the private GPU script kernel and starts it. Returns null on success, else the error text.</summary>
        Task<string?> PushKernelAsync(string username, string apiKey, string slug, string code, bool gpu, CancellationToken ct);
        /// <summary>Last lines of the kernel's console output (stdout+stderr, notebook-conversion noise removed); null if none.</summary>
        Task<string?> GetLogAsync(string username, string apiKey, string slug, int maxChars, CancellationToken ct);
        /// <summary>Kernel session state ("queued", "running", "complete", "error", "cancel...") and Kaggle's failure text; null if unknown.</summary>
        Task<(string Status, string? Failure)?> GetStatusAsync(string username, string apiKey, string slug, CancellationToken ct);
    }

    public class KaggleClient(HttpClient http) : IKaggleClient
    {
        public const string BaseUrl = "https://www.kaggle.com/";

        // Legacy keys use Basic username:key; the newer KGAT_ tokens use Bearer.
        private static HttpRequestMessage Req(HttpMethod m, string path, string username, string apiKey)
        {
            var r = new HttpRequestMessage(m, BaseUrl + path);
            r.Headers.Authorization = apiKey.StartsWith("KGAT_", StringComparison.Ordinal)
                ? new AuthenticationHeaderValue("Bearer", apiKey)
                : new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{apiKey}")));
            return r;
        }

        public async Task<bool> ValidateAsync(string username, string apiKey, CancellationToken ct)
        {
            // Kaggle answers 401 "Unauthenticated" to this call unless the credentials are valid.
            using var res = await http.SendAsync(Req(HttpMethod.Get, "api/v1/kernels/list?pageSize=1", username, apiKey), ct);
            return res.IsSuccessStatusCode;
        }

        public async Task<string?> PushKernelAsync(string username, string apiKey, string slug, string code, bool gpu, CancellationToken ct)
        {
            var req = Req(HttpMethod.Post, "api/v1/kernels/push", username, apiKey);
            req.Content = JsonContent.Create(new
            {
                slug = $"{username}/{slug}",
                newTitle = slug,
                text = code,
                language = "python",
                kernelType = "script",
                isPrivate = true,
                enableGpu = gpu,
                enableInternet = true,
                datasetDataSources = Array.Empty<string>(),
                competitionDataSources = Array.Empty<string>(),
                kernelDataSources = Array.Empty<string>(),
                modelDataSources = Array.Empty<string>(),
                categoryIds = Array.Empty<string>()
            });
            using var res = await http.SendAsync(req, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode) return $"Kaggle {(int)res.StatusCode}";
            try
            {
                var err = JsonDocument.Parse(body).RootElement;
                if (err.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(e.GetString()))
                    return e.GetString();
            }
            catch (JsonException) { }
            return null;
        }

        public async Task<string?> GetLogAsync(string username, string apiKey, string slug, int maxChars, CancellationToken ct)
        {
            try
            {
                using var res = await http.SendAsync(Req(HttpMethod.Get, $"api/v1/kernels/output?userName={Uri.EscapeDataString(username)}&kernelSlug={Uri.EscapeDataString(slug)}", username, apiKey), ct);
                if (!res.IsSuccessStatusCode) return null;
                var j = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct)).RootElement;
                if (!j.TryGetProperty("log", out var log) || log.ValueKind != JsonValueKind.String) return null;
                var text = string.Concat(JsonDocument.Parse(log.GetString()!).RootElement.EnumerateArray()
                    .Where(e => e.TryGetProperty("data", out _)).Select(e => e.GetProperty("data").GetString() ?? ""));
                var lines = text.Split('\n').Where(l => l.Trim().Length > 0 && !l.Contains("nbconvert") && !l.Contains("NbConvertApp") && !l.Contains("dist-packages") && !l.Contains("SyntaxWarning") && !l.Contains("re.sub(")).ToList();
                var tail = string.Join("\n", lines).Trim();
                return tail.Length == 0 ? null : tail[^Math.Min(tail.Length, maxChars)..];
            }
            catch (Exception ex) when (ex is JsonException or HttpRequestException or InvalidOperationException) { return null; }
        }

        public async Task<(string Status, string? Failure)?> GetStatusAsync(string username, string apiKey, string slug, CancellationToken ct)
        {
            using var res = await http.SendAsync(Req(HttpMethod.Get, $"api/v1/kernels/status?userName={Uri.EscapeDataString(username)}&kernelSlug={Uri.EscapeDataString(slug)}", username, apiKey), ct);
            if (!res.IsSuccessStatusCode) return null;
            try
            {
                var j = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct)).RootElement;
                string? Str(string n) => j.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                var status = Str("status")?.ToLowerInvariant() ?? "unknown";
                var failure = Str("failureMessage");
                if (string.IsNullOrWhiteSpace(failure) && status is "error" or "complete" or "cancel_acknowledged")
                    failure = await GetLogAsync(username, apiKey, slug, 280, ct);
                return (status, failure);
            }
            catch (JsonException) { return null; }
        }
    }
}
