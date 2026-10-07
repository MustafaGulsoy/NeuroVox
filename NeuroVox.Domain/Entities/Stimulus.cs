using NeuroVox.Domain.Entities.Common;

namespace NeuroVox.Domain.Entities
{
    public class Stimulus : BaseEntity
    {
        public Guid CustomerId { get; set; }
        public string StimulusId { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? AssetPath { get; set; }
        public Guid ProtocolId { get; set; }
    }
}
