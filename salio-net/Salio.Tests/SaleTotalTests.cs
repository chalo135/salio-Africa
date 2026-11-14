using System.Text.RegularExpressions;
using Salio.Domain.Entities;
using Salio.Domain.Enums;
using Salio.Domain.Services;

namespace Salio.Tests
{
    // A sale's total must equal the sum of its lines.
    //
    // Where the numbers are made today: SalesController.Create works out each
    // line total and the sale total itself. SaleService (Domain) only turns
    // the lines into journal lines. So this file tests two things:
    //   1. SaleService books exactly the sum of the line totals as revenue,
    //      with decimal quantities, and refuses payments that do not match
    //      that sum (plain Domain tests, no database).
    //   2. The controller's arithmetic, by reading its source code, the same
    //      way the journal and stock guard tests do. The controller itself is
    //      not run here: that needs a database.
    public class SaleTotalTests
    {
        private const string SalesControllerFile = "Salio.Api/Controllers/SalesController.cs";

        private static readonly Guid OrganizationId = Guid.NewGuid();
        private static readonly Guid CashAccountId = Guid.NewGuid();
        private static readonly Guid MpesaAccountId = Guid.NewGuid();
        private static readonly Guid SalesAccountId = Guid.NewGuid();
        private static readonly Guid CostAccountId = Guid.NewGuid();
        private static readonly Guid InventoryAccountId = Guid.NewGuid();
        private static readonly Guid ReceivableAccountId = Guid.NewGuid();

        // Three lines, two with decimal quantities. Line totals follow the
        // controller's rule: quantity times unit price, rounded once per line
        // to whole cents, a half rounded away from zero.
        //   1.5  x 333  = 499.5  -> 500
        //   2.25 x 1000 = 2250   -> 2250
        //   1    x 4550 = 4550   -> 4550
        // Sum of lines: 7300.
        private static List<SaleLine> ThreeLines() => new()
        {
            NewLine(quantity: 1.5m, unitPriceMinor: 333, lineTotalMinor: 500),
            NewLine(quantity: 2.25m, unitPriceMinor: 1000, lineTotalMinor: 2250),
            NewLine(quantity: 1m, unitPriceMinor: 4550, lineTotalMinor: 4550)
        };

        [Fact]
        public void Revenue_booked_is_the_sum_of_the_line_totals()
        {
            Sale sale = NewSale(totalMinor: 7300);

            List<JournalLine> journal = Build(sale, ThreeLines(), new()
            {
                NewPayment(PaymentMethod.Cash, 3000),
                NewPayment(PaymentMethod.Mpesa, 4300)
            });

            long salesCredit = 0;
            long debits = 0;
            long credits = 0;
            foreach (JournalLine line in journal)
            {
                if (line.AccountId == SalesAccountId)
                {
                    salesCredit = salesCredit + line.CreditMinor;
                }
                debits = debits + line.DebitMinor;
                credits = credits + line.CreditMinor;
            }

            Assert.Equal(7300, salesCredit);
            Assert.Equal(debits, credits);
        }

        [Fact]
        public void Payments_that_do_not_add_up_to_the_lines_are_refused()
        {
            Sale sale = NewSale(totalMinor: 7300);

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                Build(sale, ThreeLines(), new() { NewPayment(PaymentMethod.Cash, 7299) }));

            Assert.Equal("Payments must add up to the sale total", error.Message);
        }

        // SaleService never reads Sale.TotalMinor: it adds up the lines
        // itself. So a wrong stored total cannot leak into the books, and
        // paying the wrong stored total is refused. (The stored total is still
        // read by TaxService for turnover tax, which is why the controller
        // test below matters.)
        [Fact]
        public void A_wrong_stored_total_does_not_change_what_is_booked()
        {
            Sale sale = NewSale(totalMinor: 7299);

            Assert.Throws<InvalidOperationException>(() =>
                Build(sale, ThreeLines(), new() { NewPayment(PaymentMethod.Cash, 7299) }));
        }

        // Only SalesController creates a Sale or a SaleLine. If another place
        // starts making them, it must be checked the same way.
        [Fact]
        public void Only_SalesController_creates_sales_and_sale_lines()
        {
            Regex newSale = new(@"\bnew\s+Sale(Line)?\s*[({]|\bSale(Line)?\s+\w+\s*=\s*new\s*\(");
            List<string> problems = new();

            foreach (SourceCode.SourceFile file in SourceCode.ProductionFiles())
            {
                if (file.RelativePath == SalesControllerFile || file.RelativePath.Contains("/Migrations/"))
                {
                    continue;
                }
                if (newSale.IsMatch(file.Code))
                {
                    problems.Add(file.RelativePath);
                }
            }

            Assert.True(problems.Count == 0,
                "A Sale or SaleLine is created outside SalesController. Check its total there too:\n"
                + string.Join("\n", problems));
        }

        // Reads SalesController.Create and checks, in order:
        //   the line total is quantity x unit price, rounded half away from zero,
        //   that same figure is stored on the line,
        //   that same figure is added to the running total,
        //   the running total is what is stored on the sale,
        // and that the running total is changed nowhere else.
        [Fact]
        public void SalesController_stores_the_sum_of_the_line_totals_as_the_sale_total()
        {
            string code = SourceCode.ProductionFile(SalesControllerFile).Code;

            int lineRule = Once(code,
                @"long\s+lineTotalMinor\s*=\s*\(long\)\s*Math\.Round\(\s*lineRequest\.Quantity\s*\*\s*lineRequest\.UnitPriceMinor\s*,\s*MidpointRounding\.AwayFromZero\s*\)\s*;",
                "the line total rule: (long)Math.Round(Quantity * UnitPriceMinor, AwayFromZero)");
            int storedOnLine = Once(code,
                @"\bLineTotalMinor\s*=\s*lineTotalMinor\s*,",
                "LineTotalMinor = lineTotalMinor on the SaleLine");
            int addedUp = Once(code,
                @"\btotalMinor\s*=\s*totalMinor\s*\+\s*lineTotalMinor\s*;",
                "totalMinor = totalMinor + lineTotalMinor");
            int storedOnSale = Once(code,
                @"\bsale\.TotalMinor\s*=\s*totalMinor\s*;",
                "sale.TotalMinor = totalMinor");
            Once(code, @"\blong\s+totalMinor\s*=\s*0\s*;", "long totalMinor = 0");

            Assert.True(lineRule < storedOnLine && storedOnLine < addedUp && addedUp < storedOnSale,
                "The steps of the sale total are out of order in " + SalesControllerFile);

            // Exactly two writes to the running total: "= 0" and "+ lineTotalMinor".
            int writes = Regex.Matches(code, @"\btotalMinor\s*(\+|-|\*|/)?=(?!=)").Count;
            Assert.True(writes == 2,
                "totalMinor is changed " + writes + " times in " + SalesControllerFile
                + "; expected 2 (start at 0, add each line).");

            // Nothing else sets the sale's total.
            int saleTotalWrites = Regex.Matches(code, @"\.TotalMinor\s*=(?!=)").Count;
            Assert.True(saleTotalWrites == 1,
                "sale.TotalMinor is set " + saleTotalWrites + " times in " + SalesControllerFile + "; expected 1.");
        }

        private static int Once(string code, string pattern, string what)
        {
            MatchCollection found = Regex.Matches(code, pattern);
            Assert.True(found.Count == 1,
                "Expected exactly one '" + what + "' in " + SalesControllerFile + ", found " + found.Count
                + ". If the sale total is now worked out differently, update this test to match.");
            return found[0].Index;
        }

        private static List<JournalLine> Build(Sale sale, List<SaleLine> lines, List<Payment> payments)
        {
            SaleService service = new();
            return service.BuildJournalLines(
                sale, lines, payments,
                CashAccountId, MpesaAccountId, SalesAccountId, CostAccountId,
                InventoryAccountId, ReceivableAccountId, null);
        }

        private static Sale NewSale(long totalMinor) => new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = OrganizationId,
            SaleNumber = "S-000001",
            SaleDate = new DateOnly(2026, 11, 6),
            OccurredAt = DateTimeOffset.UtcNow,
            TotalMinor = totalMinor,
            Status = SaleStatus.Completed
        };

        private static SaleLine NewLine(decimal quantity, long unitPriceMinor, long lineTotalMinor) => new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = OrganizationId,
            SaleId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            Quantity = quantity,
            UnitPriceMinor = unitPriceMinor,
            LineTotalMinor = lineTotalMinor,
            TaxBand = 'A',
            UnitCostMinor = 0
        };

        private static Payment NewPayment(PaymentMethod method, long amountMinor) => new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = OrganizationId,
            SaleId = Guid.NewGuid(),
            Method = method,
            AmountMinor = amountMinor,
            ReceivedAt = DateTimeOffset.UtcNow
        };
    }
}
