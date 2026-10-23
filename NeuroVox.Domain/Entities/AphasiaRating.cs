using NeuroVox.Domain.Entities.Common;
using NeuroVox.Domain.Enums;

namespace NeuroVox.Domain.Entities
{
    // A rater's overall judgement of the whole recording (one per rater and recording). Blind like TherapistAnnotation:
    // raters only ever read their own. Independent of the segment labels and of any AI output.
    public class AphasiaRating : BaseEntity
    {
        public Guid CustomerId { get; set; }
        public Guid RecordingId { get; set; }
        public Guid VisitId { get; set; }
        public Guid AnnotatorId { get; set; }
        public int RaterIndex { get; set; }
        public AphasiaType Type { get; set; }
        public DateTime SubmittedAt { get; set; }
    }
}
