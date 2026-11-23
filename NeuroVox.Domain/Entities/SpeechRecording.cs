using NeuroVox.Domain.Entities.Common;
using NeuroVox.Domain.Enums;

namespace NeuroVox.Domain.Entities
{
    // RAW AUDIO layer: stored separately from measurements and annotations.
    public class SpeechRecording : BaseEntity
    {
        public Guid CustomerId { get; set; }
        public Guid VisitId { get; set; }
        public Guid StimulusId { get; set; }
        public string StimulusVersion { get; set; } = string.Empty;
        public string InstructionVersion { get; set; } = string.Empty;
        public double RecordingDurationSeconds { get; set; }
        public double? SpeechDurationSeconds { get; set; }
        public string? RecordingEnvironment { get; set; }
        public string? MicrophoneDevice { get; set; }
        public AudioQuality AudioQuality { get; set; } = AudioQuality.Unknown;
        public string? AudioFilePath { get; set; }
        public string? TranscriptText { get; set; }
        public string? TranscriptSource { get; set; }
        public string? TranscriberVersion { get; set; }
        // Interviewer ("doctor") speech to leave out of the analysis: JSON [{"start":1.2,"end":4.5},...] in seconds.
        // Source "auto" = detected by the AI host (replaced on re-analysis); "manual" = set by a therapist (always wins, never auto-overwritten).
        public string? DoctorSegmentsJson { get; set; }
        public string? DoctorSegmentsSource { get; set; }
        public string? TranscriptWordsJson { get; set; }   // [{s,e,w}] every word with its time, doctor included; the UI labels Doktor/Hasta from DoctorSegmentsJson
        public AnalysisStatus AnalysisStatus { get; set; } = AnalysisStatus.None;
        public string? AnalysisError { get; set; }
        public DateTime? AnalyzedAt { get; set; }
    }
}
