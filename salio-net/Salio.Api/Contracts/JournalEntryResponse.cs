using Salio.Domain.Enums;

namespace Salio.Api.Contracts;

public class JournalEntryResponse
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public DateOnly EntryDate { get; set; }

    public string Description { get; set; } = string.Empty;

    public SourceType SourceType { get; set; }

    public Guid? SourceId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public List<JournalLineResponse> Lines { get; set; } = new();
}
