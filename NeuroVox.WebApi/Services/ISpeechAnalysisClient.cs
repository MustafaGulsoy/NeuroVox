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
        Task<AnalysisResult?> AnalyzeAsync(AiHost host, string audioFilePath, Guid customerId, Guid visitId, Guid recordingId);
        /// <summary>Trains on the host; returns the artifact zip, or the reason it failed.</summary>
        Task<(byte[]? Zip, string? Error)> TrainAsync(AiHost host, string csv);
                /// <summary>Two-group statistics (numbers only) from any online AI host: {feature: {a:[..], b:[..]}} -> {results:[..]}.</summary>
        Task<JsonElement?> CompareAsync(Guid customerId, Dictionary<string, Dictionary<string, List<double>>> features);
        Task<JsonElement?> PredictAsync(Guid customerId, Dictionary<string, double> features);
    }
}
