using Salio.Domain.Entities;
using Salio.Domain.Enums;
using Salio.Domain.Services;

namespace Salio.Tests
{
    public class SaleServiceTests
    {
        private static readonly Guid OrganizationId = Guid.NewGuid();
        private static readonly Guid ProductId = Guid.NewGuid();

        private static readonly Guid CashAccountId = Guid.NewGuid();
        private static readonly Guid MpesaAccountId = Guid.NewGuid();
        private static readonly Guid SalesAccountId = Guid.NewGuid();
        private static readonly Guid CostOfGoodsSoldAccountId = Guid.NewGuid();
        private static readonly Guid InventoryAccountId = Guid.NewGuid();

        [Fact]
        public void A_cash_sale_produces_four_balanced_journal_lines()
        {
            Sale sale = NewSale(totalMinor: 5000);

            List<SaleLine> saleLines = new()
            {
                NewSaleLine(quantity: 1, unitPriceMinor: 5000, lineTotalMinor: 5000, unitCostMinor: 3500)
            };

            List<Payment> payments = new()
            {
                NewPayment(PaymentMethod.Cash, 5000)
            };

            SaleService service = new();

            List<JournalLine> journalLines = service.BuildJournalLines(
                sale,
                saleLines,
                payments,
                CashAccountId,
                MpesaAccountId,
                SalesAccountId,
                CostOfGoodsSoldAccountId,
                InventoryAccountId);

            Assert.Equal(4, journalLines.Count);

            // What the customer paid.
            Assert.Equal(5000, DebitFor(journalLines, CashAccountId));
            Assert.Equal(5000, CreditFor(journalLines, SalesAccountId));

            // What leaving the shelf cost the shop.
            Assert.Equal(3500, DebitFor(journalLines, CostOfGoodsSoldAccountId));
            Assert.Equal(3500, CreditFor(journalLines, InventoryAccountId));
        }

        [Fact]
        public void The_four_lines_balance()
        {
            Sale sale = NewSale(totalMinor: 5000);

            List<SaleLine> saleLines = new()
            {
                NewSaleLine(quantity: 1, unitPriceMinor: 5000, lineTotalMinor: 5000, unitCostMinor: 3500)
            };

            List<Payment> payments = new()
            {
                NewPayment(PaymentMethod.Cash, 5000)
            };

            SaleService service = new();

            List<JournalLine> journalLines = service.BuildJournalLines(
                sale,
                saleLines,
                payments,
                CashAccountId,
                MpesaAccountId,
                SalesAccountId,
                CostOfGoodsSoldAccountId,
                InventoryAccountId);

            long totalDebits = 0;
            long totalCredits = 0;

            foreach (var line in journalLines)
            {
                totalDebits = totalDebits + line.DebitMinor;
                totalCredits = totalCredits + line.CreditMinor;

                // Rule 2: every row is scoped to the organisation.
                Assert.Equal(OrganizationId, line.OrganizationId);
            }

            Assert.Equal(totalDebits, totalCredits);
        }

        [Fact]
        public void A_sale_with_no_recorded_cost_skips_the_cost_lines()
        {
            Sale sale = NewSale(totalMinor: 5000);

            // Never received into stock, so there is no cost on record.
            List<SaleLine> saleLines = new()
            {
                NewSaleLine(quantity: 1, unitPriceMinor: 5000, lineTotalMinor: 5000, unitCostMinor: 0)
            };

            List<Payment> payments = new()
            {
                NewPayment(PaymentMethod.Cash, 5000)
            };

            SaleService service = new();

            List<JournalLine> journalLines = service.BuildJournalLines(
                sale,
                saleLines,
                payments,
                CashAccountId,
                MpesaAccountId,
                SalesAccountId,
                CostOfGoodsSoldAccountId,
                InventoryAccountId);

            Assert.Equal(2, journalLines.Count);
            Assert.Equal(5000, DebitFor(journalLines, CashAccountId));
            Assert.Equal(5000, CreditFor(journalLines, SalesAccountId));

            // The ledger must accept what the sale produced.
            new LedgerService().PostJournal(new JournalEntry { Id = Guid.NewGuid() }, journalLines);
        }

        [Fact]
        public void A_sale_with_no_lines_throws()
        {
            Sale sale = NewSale(totalMinor: 0);

            List<SaleLine> saleLines = new();

            SaleService service = new();

            Assert.Throws<InvalidOperationException>(() =>
            {
                service.BuildJournalLines(
                    sale,
                    saleLines,
                    new List<Payment>(),
                    CashAccountId,
                    MpesaAccountId,
                    SalesAccountId,
                    CostOfGoodsSoldAccountId,
                    InventoryAccountId);
            });
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

        private static Sale NewSale(long totalMinor) => new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = OrganizationId,
            SaleNumber = "S-0001",
            SaleDate = new DateOnly(2026, 9, 8),
            OccurredAt = DateTimeOffset.UtcNow,
            TotalMinor = totalMinor,
            Status = SaleStatus.Completed
        };

        private static Payment NewPayment(PaymentMethod method, long amountMinor) => new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = OrganizationId,
            SaleId = Guid.NewGuid(),
            Method = method,
            AmountMinor = amountMinor,
            Reference = null,
            ReceivedAt = DateTimeOffset.UtcNow
        };

        private static SaleLine NewSaleLine(
            decimal quantity,
            long unitPriceMinor,
            long lineTotalMinor,
            long unitCostMinor) => new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = OrganizationId,
            SaleId = Guid.NewGuid(),
            ProductId = ProductId,
            Quantity = quantity,
            UnitPriceMinor = unitPriceMinor,
            LineTotalMinor = lineTotalMinor,
            UnitCostMinor = unitCostMinor,
            TaxBand = 'A'
        };
    }
}
