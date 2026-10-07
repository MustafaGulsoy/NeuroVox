using System.Net.Http.Json;
using System.Text.Json;

namespace NeuroVox.WebApi.Services
{
    public class SpeechAnalysisClient : ISpeechAnalysisClient
    {
        private readonly HttpClient _http;
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true
        };

        public SpeechAnalysisClient(HttpClient http) => _http = http;

        public async Task<AnalysisResult?> AnalyzeAsync(string audioFilePath, Guid customerId, Guid visitId, Guid recordingId)
        {
            var payload = new Dictionary<string, object>
            {
                ["audio_path"] = audioFilePath,
                ["customer_id"] = customerId,
                ["visit_id"] = visitId,
                ["recording_id"] = recordingId,
                ["language"] = "tr"
            };
            AnalysisResult? result = null;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    var response = await _http.PostAsJsonAsync("analyze", payload, JsonOptions);
                    if (!response.IsSuccessStatusCode)
                        return null;
                    return await response.Content.ReadFromJsonAsync<AnalysisResult>(JsonOptions);
                }
                catch (HttpRequestException) when (attempt < 2)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1 * (attempt + 1)));
                }
            }
            return result;
        }

        public async Task<JsonElement?> PredictAsync(Dictionary<string, double> features)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    var response = await _http.PostAsJsonAsync("predict", new { features }, JsonOptions);
                    if (response.IsSuccessStatusCode)
                        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
                    return null;
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
