using Salio.Domain.Enums;
using Salio.Domain.Services;

namespace Salio.Tests
{
    // What the quantities in an import mean: the owner says, these check it.
    public class ImportStockBasisRulesTests
    {
        [Fact]
        public void A_file_without_quantities_is_None_whatever_was_sent()
        {
            var decision = ImportStockBasisRules.Decide(false, "Purchase", "0");

            Assert.Null(decision.Error);
            Assert.Equal(ImportStockBasis.None, decision.Basis);
            Assert.Null(decision.PaidWith);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("None")]
        [InlineData("none")]
        public void Quantities_without_an_answer_are_refused(string? stockBasis)
        {
            var decision = ImportStockBasisRules.Decide(true, stockBasis, null);

            Assert.NotNull(decision.Error);
            Assert.Contains("stock you bought", decision.Error);
        }

        [Fact]
        public void An_answer_Salio_does_not_know_is_refused()
        {
            var decision = ImportStockBasisRules.Decide(true, "Gift", null);

            Assert.NotNull(decision.Error);
        }

        [Theory]
        [InlineData("OpeningStock")]
        [InlineData("openingstock")]
        [InlineData(" OPENINGSTOCK ")]
        public void Opening_stock_is_accepted_in_any_capitals_and_needs_no_payment(string stockBasis)
        {
            var decision = ImportStockBasisRules.Decide(true, stockBasis, "3");

            Assert.Null(decision.Error);
            Assert.Equal(ImportStockBasis.OpeningStock, decision.Basis);
            Assert.Null(decision.PaidWith);
        }

        [Theory]
        [InlineData("0", PaymentMethod.Cash)]
        [InlineData("1", PaymentMethod.Mpesa)]
        [InlineData("3", PaymentMethod.Credit)]
        public void A_purchase_takes_the_Stock_in_payment_numbers(string paidWith, PaymentMethod expected)
        {
            var decision = ImportStockBasisRules.Decide(true, "purchase", paidWith);

            Assert.Null(decision.Error);
            Assert.Equal(ImportStockBasis.Purchase, decision.Basis);
            Assert.Equal(expected, decision.PaidWith);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("2")]
        [InlineData("9")]
        [InlineData("Cash")]
        public void A_purchase_without_a_known_payment_is_refused(string? paidWith)
        {
            var decision = ImportStockBasisRules.Decide(true, "Purchase", paidWith);

            Assert.NotNull(decision.Error);
        }
    }
}
