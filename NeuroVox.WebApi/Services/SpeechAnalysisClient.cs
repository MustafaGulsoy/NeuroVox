using System.Net.Http.Json;
using System.Text.Json;

namespace NeuroVox.WebApi.Services
{
    public class SpeechAnalysisClient(HttpClient http, AudioStorage audio, AiHostPool pool, ModelStorage models, IConfiguration config) : ISpeechAnalysisClient
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true
        };

        private static HttpRequestMessage Req(AiHost host, string path, HttpContent content)
        {
            var r = new HttpRequestMessage(HttpMethod.Post, new Uri(host.BaseUrl, path)) { Content = content };
            if (host.ApiKey.Length > 0) r.Headers.Add("X-Api-Key", host.ApiKey);
            return r;
        }

        public async Task<AnalysisResult?> AnalyzeAsync(AiHost host, string audioFilePath, Guid customerId, Guid visitId, Guid recordingId)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    FileStream? fs = null;
                    HttpRequestMessage req;
                    if (host.Upload)
                    {
                        fs = File.OpenRead(audio.Resolve(audioFilePath));
                        req = Req(host, "analyze-upload", new MultipartFormDataContent { { new StreamContent(fs), "file", Path.GetFileName(audioFilePath) }, { new StringContent("tr"), "language" } });
                    }
                    else
                        req = Req(host, "analyze", JsonContent.Create(new Dictionary<string, object>
                        {
                            ["audio_path"] = audioFilePath, ["customer_id"] = customerId, ["visit_id"] = visitId, ["recording_id"] = recordingId, ["language"] = "tr"
                        }, options: JsonOptions));
                    using (req)
                    using (fs)
                    {
                        var response = await http.SendAsync(req);
                        if (!response.IsSuccessStatusCode) return null;
                        return await response.Content.ReadFromJsonAsync<AnalysisResult>(JsonOptions);
                    }
                }
                catch (HttpRequestException) when (attempt < 2)
                {
                    await Task.Delay(TimeSpan.FromSeconds(3 * (attempt + 1)));   // fresh tunnels need a few seconds to resolve
                }
            }
            return null;
        }

        public async Task<(byte[]? Zip, string? Error)> TrainAsync(AiHost host, string csv)
        {
            try
            {
                using var req = Req(host, "train", new MultipartFormDataContent
                {
                    { new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(csv)), "file", "training-set.csv" }
                });
                using var res = await http.SendAsync(req);
                if (res.IsSuccessStatusCode) return (await res.Content.ReadAsByteArrayAsync(), null);
                var body = await res.Content.ReadAsStringAsync();
                return (null, $"AI host {(int)res.StatusCode}: {(body.Length > 200 ? body[..200] : body)}");
            }
            catch (HttpRequestException ex) { return (null, ex.Message); }
        }

        // Light calls (numbers only) go to any online host of the tenant -- no exclusive lease needed.
        private async Task<AiHost?> AnyHostAsync(Guid customerId) => (await pool.OnlineHostsAsync(customerId, CancellationToken.None)).FirstOrDefault();

        public async Task<JsonElement?> CompareAsync(Guid customerId, Dictionary<string, Dictionary<string, List<double>>> features)
        {
            var host = await AnyHostAsync(customerId);
            if (host is null) return null;
            try
            {
                using var req = Req(host, "stats/compare-many", JsonContent.Create(new { features }, options: JsonOptions));
                var response = await http.SendAsync(req);
                return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions) : null;
            }
            catch (HttpRequestException) { return null; }
        }

        public async Task<JsonElement?> PredictAsync(Guid customerId, Dictionary<string, double> features)
        {
            var host = await AnyHostAsync(customerId);
            if (host is null) return null;
            var name = config["NeuroVox:ModelName"] ?? "logistic_regression";
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    HttpContent body;
                    string path;
                    if (host.Key == "local") { path = "predict"; body = JsonContent.Create(new { features }, options: JsonOptions); }
                    else
                    {
                        // Remote hosts do not share the model volume: send the (small) current model with the request.
                        var m = models.ReadCurrent(customerId, name);
                        if (m is null) return null;
                        path = "predict-upload";
                        body = new MultipartFormDataContent
                        {
                            { new ByteArrayContent(m.Value.Model), "model", name + ".joblib" },
                            { new StringContent(m.Value.FeatureOrder), "feature_order" },
                            { new StringContent(JsonSerializer.Serialize(features)), "features" }
                        };
                    }
                    using var req = Req(host, path, body);
                    var response = await http.SendAsync(req);
                    return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions) : null;
                }
                catch (HttpRequestException) when (attempt < 2)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1 * (attempt + 1)));
                }
            }
            return null;
        }
    }
}
