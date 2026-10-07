using NeuroVox.Domain.Entities.Common;

namespace NeuroVox.Domain.Entities
{
    public class Participant : BaseEntity
    {
        public Guid CustomerId { get; set; }
        public string ParticipantCode { get; set; } = string.Empty;
        public DateOnly? DateOfBirth { get; set; }
        public string? Sex { get; set; }
        public string? Notes { get; set; }
    }
}
