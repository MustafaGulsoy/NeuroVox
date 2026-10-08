using NeuroVox.Domain.Entities.Common;

namespace NeuroVox.Domain.Entities
{
    // A Kaggle account whose GPU runs the AI service during the test phase. Secrets are AES-GCM encrypted
    // (SecretProtector); the plain Kaggle token and AI key are never returned by the API.
    public class KaggleAccount : BaseEntity
    {
        public Guid CustomerId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string EncryptedApiKey { get; set; } = string.Empty;   // Kaggle API token
        public string? EncryptedAiKey { get; set; }                   // X-Api-Key of the kernel's AI service (rotated per connect)
        public string? RegisterTokenHash { get; set; }                // SHA-256 of the one-time token the kernel presents
        public string? PublicUrl { get; set; }                        // tunnel URL reported by the kernel
        public DateTime? LastHeartbeatUtc { get; set; }
        public DateTime? LastConnectAttemptUtc { get; set; }
        public string? LastError { get; set; }
        // GPU quota bookkeeping (Kaggle gives ~30 GPU h/week; there is no API to read it, so heartbeats are summed).
        public double GpuSecondsThisWeek { get; set; }
        public DateTime? QuotaWeekStartUtc { get; set; }
        public bool RunningOnGpu { get; set; }                       // what the live kernel reported
        public DateTime? GpuExhaustedUntilUtc { get; set; }          // Kaggle refused a GPU session: use CPU until then
        public DateTime? KernelStartedUtc { get; set; }
        public string? CreatedBy { get; set; }
    }
}
