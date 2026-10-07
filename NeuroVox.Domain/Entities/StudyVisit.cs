using NeuroVox.Domain.Entities.Common;
using NeuroVox.Domain.Enums;

namespace NeuroVox.Domain.Entities
{
    public class StudyVisit : BaseEntity
    {
        public Guid CustomerId { get; set; }
        public Guid ParticipantId { get; set; }
        public Guid ProtocolId { get; set; }
        public VisitType VisitType { get; set; }
        public DateOnly? ScheduledDate { get; set; }
        public DateOnly? ActualDate { get; set; }
        public string? Notes { get; set; }
    }
}
