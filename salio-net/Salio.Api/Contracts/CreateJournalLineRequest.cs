using System.ComponentModel.DataAnnotations;

namespace Salio.Api.Contracts;

/// <summary>
/// One line of a journal entry, as it arrives from the caller.
/// Deliberately not the JournalLine entity: a caller must not be able to set
/// Id or JournalEntryId.
/// </summary>
public class CreateJournalLineRequest
{
    [Required]
    public Guid AccountId { get; set; }

    public long DebitMinor { get; set; }

    public long CreditMinor { get; set; }

    public string Currency { get; set; } = "KES";
}
