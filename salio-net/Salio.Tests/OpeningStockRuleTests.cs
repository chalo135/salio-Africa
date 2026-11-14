using System.Text.RegularExpressions;
using Salio.Domain.Enums;
using Salio.Infrastructure.Invoices;

namespace Salio.Tests
{
    // Opening stock credits Owner's Equity, so it is only allowed before the
    // shop's first sale. The last run narrowed that rule for import and
    // quick-add ("only if these parts have sold"), which booked bought stock
    // as equity. These tests keep the one rule in OpeningStockWriter, with no
    // way round it. They read the source files; no database is used.
    public class OpeningStockRuleTests
    {
        [Fact]
        public void OpeningStockWriter_always_refuses_once_the_shop_has_sold()
        {
            SourceCode.SourceFile writer = SourceCode.ProductionFile("Salio.Infrastructure/OpeningStockWriter.cs");

            Match write = Regex.Match(writer.Code, @"\bWriteAsync\s*\(");
            Assert.True(write.Success, "OpeningStockWriter has no WriteAsync.");

            string body = writer.Code.Substring(write.Index);
            Match check = Regex.Match(body, @"\bif\s*\(\s*await\s+ShopHasSoldAsync\s*\(");
            Match firstPost = Regex.Match(body, @"_journalPoster\s*\.\s*PostAsync\s*\(");

            Assert.True(check.Success, "OpeningStockWriter.WriteAsync no longer asks whether the shop has sold.");
            Assert.True(firstPost.Success);
            Assert.True(check.Index < firstPost.Index, "The sale check must come before anything is posted.");
        }

        [Fact]
        public void Nothing_can_ask_for_a_narrower_sale_check()
        {
            foreach (SourceCode.SourceFile file in SourceCode.ProductionFiles())
            {
                Assert.DoesNotMatch(@"\bOpeningStockSaleCheck\b|\bNoSaleOfTheseProducts\b", file.Code);
            }
        }

        [Fact]
        public void A_closed_period_is_named_by_month_with_the_way_out()
        {
            string message = SupplierInvoiceService.ClosedPeriodMessage(new DateOnly(2026, 10, 14), TaxType.Vat);

            Assert.Equal(
                "October 2026's VAT return has been filed, so nothing can be posted into it. "
                + "You can post this receipt today instead.",
                message);
        }
    }
}
