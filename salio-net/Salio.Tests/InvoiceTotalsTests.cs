using Salio.Domain.Entities;
using Salio.Domain.Enums;
using Salio.Domain.Services;

namespace Salio.Tests
{
    public class InvoiceTotalsTests
    {
        private static InvoiceTotalsLine Line(decimal quantity, long unitCostMinor, bool accepted = true) =>
            new() { Quantity = quantity, UnitCostMinor = unitCostMinor, Accepted = accepted };

        [Fact]
        public void Totals_agree_when_the_printed_total_equals_the_lines()
        {
            var result = InvoiceTotals.Check(110_000, new() { Line(10, 4_500), Line(4, 16_250) });

            Assert.Equal(110_000, result.LinesTotalMinor);
            Assert.True(result.TotalsAgree);
            Assert.Equal(0, result.DifferenceMinor);
        }

        [Fact]
        public void A_difference_is_invoice_total_minus_lines()
        {
            var result = InvoiceTotals.Check(3_084_000, new() { Line(10, 45_000), Line(4, 235_000), Line(12, 68_000),
                Line(20, 22_000), Line(6, 38_000), Line(10, 15_000) });

            Assert.Equal(3_024_000, result.LinesTotalMinor);
            Assert.False(result.TotalsAgree);
            Assert.Equal(60_000, result.DifferenceMinor);
        }

        [Fact]
        public void Unticked_lines_are_left_out_of_the_lines_total()
        {
            var result = InvoiceTotals.Check(50_000, new() { Line(10, 4_500), Line(1, 5_000, accepted: false) });

            Assert.Equal(45_000, result.LinesTotalMinor);
            Assert.Equal(5_000, result.DifferenceMinor);
            Assert.False(result.TotalsAgree);
        }

        [Fact]
        public void An_unreadable_total_never_agrees_and_shows_no_difference()
        {
            var result = InvoiceTotals.Check(null, new() { Line(2, 1_000) });

            Assert.Equal(2_000, result.LinesTotalMinor);
            Assert.False(result.TotalsAgree);
            Assert.Equal(0, result.DifferenceMinor);
        }

        // The figure the screen shows must be the figure the stock receipt
        // books: the same rounding as StockReceiptService.TotalCost.
        [Theory]
        [InlineData(1.5, 333)]
        [InlineData(0.333, 1_001)]
        [InlineData(2.005, 99)]
        [InlineData(0.5, 1)]
        [InlineData(7, 4_550)]
        public void A_line_rounds_exactly_as_the_stock_receipt_does(double quantityAsDouble, long unitCostMinor)
        {
            decimal quantity = (decimal)quantityAsDouble;

            var movement = new StockMovement
            {
                Type = StockMovementType.Receipt,
                QuantityChange = quantity,
                UnitCostMinor = unitCostMinor
            };

            long receipt = new StockReceiptService().TotalCost(new List<StockMovement> { movement });

            Assert.Equal(receipt, InvoiceTotals.LineTotal(quantity, unitCostMinor));
            Assert.Equal(receipt, InvoiceTotals.Check(null, new() { Line(quantity, unitCostMinor) }).LinesTotalMinor);
        }
    }
}
