using Salio.Domain.Entities;
using Salio.Domain.Services;

namespace Salio.Tests
{
    public class CustomerPaymentServiceTests
    {
        private static readonly Guid OrganizationId = Guid.NewGuid();
        private static readonly Guid CustomerId = Guid.NewGuid();
        private static readonly Guid CashAccountId = Guid.NewGuid();
        private static readonly Guid ReceivableAccountId = Guid.NewGuid();

        [Fact]
        public void A_part_payment_debits_cash_and_credits_receivable_for_the_customer()
        {
            // Owes 4,000, brings 2,000: normal, and accepted.
            List<JournalLine> lines = Build(amountMinor: 200000, owedMinor: 400000);

            Assert.Equal(2, lines.Count);

            Assert.Equal(CashAccountId, lines[0].AccountId);
            Assert.Equal(200000, lines[0].DebitMinor);
            Assert.Null(lines[0].CustomerId);

            Assert.Equal(ReceivableAccountId, lines[1].AccountId);
            Assert.Equal(200000, lines[1].CreditMinor);
            Assert.Equal(CustomerId, lines[1].CustomerId);

            new LedgerService().PostJournal(new JournalEntry { Id = Guid.NewGuid() }, lines);
        }

        [Fact]
        public void Paying_off_the_whole_balance_is_accepted()
        {
            List<JournalLine> lines = Build(amountMinor: 400000, owedMinor: 400000);

            Assert.Equal(400000, lines[1].CreditMinor);
        }

        [Fact]
        public void Paying_more_than_is_owed_is_refused_and_says_the_balance()
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                Build(amountMinor: 500000, owedMinor: 400000));

            Assert.Contains("Wanjiku owes KES 4000.00", ex.Message);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-100)]
        public void A_zero_or_negative_payment_is_refused(long amountMinor)
        {
            Assert.Throws<InvalidOperationException>(() =>
                Build(amountMinor: amountMinor, owedMinor: 400000));
        }

        private static List<JournalLine> Build(long amountMinor, long owedMinor)
        {
            return new CustomerPaymentService().BuildJournalLines(
                OrganizationId, CustomerId, "Wanjiku", CashAccountId, ReceivableAccountId,
                amountMinor, owedMinor);
        }
    }
}
