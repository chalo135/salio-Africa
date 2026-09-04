using Salio.Domain.Enums;

namespace Salio.Domain.Entities
{
    public class JournalEntry
    {
        public Guid Id { get; set; }
        public Guid OrganizationId { get; set; }
        public DateOnly EntryDate { get; set; }
        public string Description { get; set; } = string.Empty;
        public SourceType SourceType { get; set; }
        public Guid? SourceId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }
}
