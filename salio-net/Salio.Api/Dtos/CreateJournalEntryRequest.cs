using System.ComponentModel.DataAnnotations;
using Salio.Domain.Enums;

namespace Salio.Api.Dtos;

/// <summary>
/// A journal entry as it arrives from the caller.
/// Deliberately not the JournalEntry entity: a caller must not be able to set
/// Id or CreatedAt.
/// </summary>
public class CreateJournalEntryRequest
{
    // TODO: this must come from the authenticated user once authentication
    // exists. Until then any caller can post into any organisation, which is
    // only acceptable in development.
    [Required]
    public Guid OrganizationId { get; set; }

    [Required]
    public DateOnly EntryDate { get; set; }

    [Required]
    [MaxLength(500)]
    public string Description { get; set; } = string.Empty;

    public SourceType SourceType { get; set; }

    public Guid? SourceId { get; set; }

    [Required]
    public List<CreateJournalLineRequest> Lines { get; set; } = new();
}
