using System.Text.Json;

namespace NeuroVox.WebApi.Services
{
    public class MeasurementDto
    {
        public string FeatureName { get; set; } = string.Empty;
        public double? NumericValue { get; set; }
        public string? TextValue { get; set; }
        public string? DefinitionVersion { get; set; }
        public string? SoftwareVersion { get; set; }
        public string? ModelVersion { get; set; }
        public bool IsCandidateAnnotation { get; set; } = true;
    }

    public class AnalysisResult
    {
        public string? Transcript { get; set; }
        public string? SttModelVersion { get; set; }
        public string? SttLanguage { get; set; }
        public double? SpeechDurationSeconds { get; set; }
        public List<MeasurementDto> Measurements { get; set; } = new();
    }

    public interface ISpeechAnalysisClient
    {
        Task<AnalysisResult?> AnalyzeAsync(string audioFilePath, Guid customerId, Guid visitId, Guid recordingId);
        Task<JsonElement?> PredictAsync(Dictionary<string, double> features);
    }
}
