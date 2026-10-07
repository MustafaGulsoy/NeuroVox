namespace NeuroVox.Domain.Entities.Common
{
    public class BaseEntity
    {
        public Guid Id { get; set; }
        public DateTime RowCreatedDate { get; set; } = DateTime.UtcNow;
        public virtual DateTime? RowUpdatedDate { get; set; }
        public virtual bool RowIsActive { get; set; } = true;
        public virtual bool RowIsDeleted { get; set; } = false;
    }
}
