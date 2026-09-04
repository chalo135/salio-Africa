using Salio.Domain.Entities;
using Salio.Domain.Enums;
using Salio.Domain.Services;

namespace Salio.Tests
{
    public class LedgerServiceTests
    {
        private static readonly Guid OrganizationId = Guid.NewGuid();
        private static readonly Guid CashAccountId = Guid.NewGuid();
        private static readonly Guid SalesAccountId = Guid.NewGuid();

        [Fact]
        public void PostJournal_accepts_a_balanced_entry()
        {
            JournalEntry entry = NewEntry();
            List<JournalLine> lines = new()
            {
                NewLine(CashAccountId, debitMinor: 4550, creditMinor: 0),
                NewLine(SalesAccountId, debitMinor: 0, creditMinor: 4550)
            };

            LedgerService service = new();

            service.PostJournal(entry, lines);
        }

        [Fact]
        public void PostJournal_throws_when_debits_do_not_equal_credits()
        {
            JournalEntry entry = NewEntry();
            List<JournalLine> lines = new()
            {
                NewLine(CashAccountId, debitMinor: 4550, creditMinor: 0),
                NewLine(SalesAccountId, debitMinor: 0, creditMinor: 4000)
            };

            LedgerService service = new();

            Assert.Throws<InvalidOperationException>(
                () => service.PostJournal(entry, lines));
        }

        private static JournalEntry NewEntry() => new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = OrganizationId,
            EntryDate = new DateOnly(2026, 8, 27),
            Description = "Cash sale",
            SourceType = SourceType.Manual,
            CreatedAt = DateTimeOffset.UtcNow
        };

        private static JournalLine NewLine(Guid accountId, long debitMinor, long creditMinor) => new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = OrganizationId,
            AccountId = accountId,
            DebitMinor = debitMinor,
            CreditMinor = creditMinor,
            Currency = "KES"
        };
    }
}
