using Salio.Domain.Entities;
using Salio.Domain.Services;

namespace Salio.Tests
{
    public class ExpenseServiceTests
    {
        private static readonly Guid OrganizationId = Guid.NewGuid();
        private static readonly Guid ElectricityAccountId = Guid.NewGuid();
        private static readonly Guid CashAccountId = Guid.NewGuid();

        [Fact]
        public void An_expense_debits_the_category_and_credits_the_money_account()
        {
            List<JournalLine> lines = new ExpenseService().BuildJournalLines(
                OrganizationId, ElectricityAccountId, CashAccountId, 150000);

            Assert.Equal(2, lines.Count);
            Assert.Equal(ElectricityAccountId, lines[0].AccountId);
            Assert.Equal(150000, lines[0].DebitMinor);
            Assert.Equal(CashAccountId, lines[1].AccountId);
            Assert.Equal(150000, lines[1].CreditMinor);

            // The ledger must accept what the expense produced.
            new LedgerService().PostJournal(new JournalEntry { Id = Guid.NewGuid() }, lines);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-100)]
        public void A_zero_or_negative_amount_throws(long amountMinor)
        {
            Assert.Throws<InvalidOperationException>(() =>
                new ExpenseService().BuildJournalLines(
                    OrganizationId, ElectricityAccountId, CashAccountId, amountMinor));
        }
    }
}
