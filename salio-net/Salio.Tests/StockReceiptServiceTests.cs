using Salio.Domain.Entities;
using Salio.Domain.Enums;
using Salio.Domain.Services;

namespace Salio.Tests
{
    public class StockReceiptServiceTests
    {
        private static readonly Guid OrganizationId = Guid.NewGuid();

        private static readonly Guid CashAccountId = Guid.NewGuid();
        private static readonly Guid MpesaAccountId = Guid.NewGuid();
        private static readonly Guid AccountsPayableAccountId = Guid.NewGuid();
        private static readonly Guid InventoryAccountId = Guid.NewGuid();

        [Fact]
        public void A_cash_receipt_debits_inventory_and_credits_cash_equally()
        {
            List<StockMovement> movements = new()
            {
                NewReceiptMovement(quantity: 4, unitCostMinor: 2500)
            };

            List<JournalLine> journalLines = Build(movements, PaymentMethod.Cash);

            Assert.Equal(2, journalLines.Count);
            Assert.Equal(10000, DebitFor(journalLines, InventoryAccountId));
            Assert.Equal(10000, CreditFor(journalLines, CashAccountId));

            // The ledger must accept what the receipt produced.
            new LedgerService().PostJournal(new JournalEntry { Id = Guid.NewGuid() }, journalLines);
        }

        [Fact]
        public void A_credit_receipt_debits_inventory_and_credits_accounts_payable()
        {
            List<StockMovement> movements = new()
            {
                NewReceiptMovement(quantity: 2, unitCostMinor: 7500)
            };

            List<JournalLine> journalLines = Build(movements, PaymentMethod.Credit);

            Assert.Equal(2, journalLines.Count);
            Assert.Equal(15000, DebitFor(journalLines, InventoryAccountId));
            Assert.Equal(15000, CreditFor(journalLines, AccountsPayableAccountId));

            // Nothing was paid yet, so no money account is touched.
            Assert.Equal(-1, CreditFor(journalLines, CashAccountId));
            Assert.Equal(-1, CreditFor(journalLines, MpesaAccountId));

            foreach (var line in journalLines)
            {
                // Rule 2: every row is scoped to the organisation.
                Assert.Equal(OrganizationId, line.OrganizationId);
            }
        }

        [Fact]
        public void A_receipt_with_several_lines_totals_every_line()
        {
            List<StockMovement> movements = new()
            {
                NewReceiptMovement(quantity: 3, unitCostMinor: 1000), //  3,000
                NewReceiptMovement(quantity: 2.5m, unitCostMinor: 401), //  1,002.5 -> 1,003
                NewReceiptMovement(quantity: 10, unitCostMinor: 0) //      0: a free sample
            };

            List<JournalLine> journalLines = Build(movements, PaymentMethod.Mpesa);

            Assert.Equal(4003, new StockReceiptService().TotalCost(movements));
            Assert.Equal(4003, DebitFor(journalLines, InventoryAccountId));
            Assert.Equal(4003, CreditFor(journalLines, MpesaAccountId));
        }

        private static List<JournalLine> Build(List<StockMovement> movements, PaymentMethod paidWith)
        {
            return new StockReceiptService().BuildJournalLines(
                OrganizationId,
                movements,
                paidWith,
                CashAccountId,
                MpesaAccountId,
                AccountsPayableAccountId,
                InventoryAccountId);
        }

        private static long DebitFor(List<JournalLine> journalLines, Guid accountId)
        {
            foreach (var line in journalLines)
            {
                if (line.AccountId == accountId)
                {
                    return line.DebitMinor;
                }
            }

            return -1;
        }

        private static long CreditFor(List<JournalLine> journalLines, Guid accountId)
        {
            foreach (var line in journalLines)
            {
                if (line.AccountId == accountId)
                {
                    return line.CreditMinor;
                }
            }

            return -1;
        }

        private static StockMovement NewReceiptMovement(decimal quantity, long unitCostMinor) => new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = OrganizationId,
            ProductId = Guid.NewGuid(),
            Type = StockMovementType.Receipt,
            QuantityChange = quantity,
            UnitCostMinor = unitCostMinor,
            OccurredAt = DateTimeOffset.UtcNow
        };
    }
}
