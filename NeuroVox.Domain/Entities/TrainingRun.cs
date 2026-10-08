using NeuroVox.Domain.Entities.Common;
using NeuroVox.Domain.Enums;

namespace NeuroVox.Domain.Entities
{
    // One model-training job. It waits in the shared AI job queue until a Kaggle kernel (or the local AI host) is online.
    public class TrainingRun : BaseEntity
    {
        public Guid CustomerId { get; set; }
        public AnalysisStatus Status { get; set; } = AnalysisStatus.Queued;
        public string? Error { get; set; }
        public int SampleCount { get; set; }
        public string? ReportJson { get; set; }
        public string? ArtifactPath { get; set; }   // relative to the model storage root
        public string? RequestedBy { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? FinishedAt { get; set; }
    }
}
