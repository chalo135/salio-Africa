using Salio.Domain.Entities;
using Salio.Domain.Enums;
using Salio.Domain.Services;

namespace Salio.Tests
{
    public class StockServiceTests
    {
        private static readonly Guid OrganizationId = Guid.NewGuid();
        private static readonly Guid ProductId = Guid.NewGuid();
        private static readonly DateTimeOffset Start =
            new(2026, 8, 1, 9, 0, 0, TimeSpan.FromHours(3));

        [Fact]
        public void One_receipt_gives_that_receipts_unit_cost()
        {
            List<StockMovement> movements = new()
            {
                Receipt(quantity: 10, unitCostMinor: 3000, dayOffset: 0)
            };

            StockService service = new();

            Assert.Equal(3000, service.CalculateWeightedAverageCost(movements));
        }

        [Fact]
        public void Two_equal_receipts_average_their_costs()
        {
            List<StockMovement> movements = new()
            {
                Receipt(quantity: 10, unitCostMinor: 3000, dayOffset: 0),
                Receipt(quantity: 10, unitCostMinor: 4000, dayOffset: 1)
            };

            StockService service = new();

            Assert.Equal(3500, service.CalculateWeightedAverageCost(movements));
        }

        [Fact]
        public void A_sale_leaves_remaining_stock_at_the_running_average()
        {
            List<StockMovement> movements = new()
            {
                Receipt(quantity: 10, unitCostMinor: 3000, dayOffset: 0),
                Sale(quantity: 5, dayOffset: 1),
                Receipt(quantity: 10, unitCostMinor: 4000, dayOffset: 2)
            };

            StockService service = new();

            // 5 left at 3000 = 15000, plus 10 at 4000 = 40000.
            // 55000 / 15 = 3666.67, rounded to 3667.
            Assert.Equal(3667, service.CalculateWeightedAverageCost(movements));
        }

        [Fact]
        public void No_movements_gives_zero()
        {
            List<StockMovement> movements = new();

            StockService service = new();

            Assert.Equal(0, service.CalculateWeightedAverageCost(movements));
        }

        [Fact]
        public void Selling_five_of_ten_leaves_five()
        {
            List<StockMovement> movements = new()
            {
                Receipt(quantity: 10, unitCostMinor: 3000, dayOffset: 0),
                Sale(quantity: 5, dayOffset: 1)
            };

            StockService service = new();

            Assert.Equal(5m, service.CurrentQuantity(movements));
        }

        [Fact]
        public void No_movements_gives_zero_quantity()
        {
            List<StockMovement> movements = new();

            StockService service = new();

            Assert.Equal(0m, service.CurrentQuantity(movements));
        }

        private static StockMovement Receipt(decimal quantity, long unitCostMinor, int dayOffset) =>
            NewMovement(StockMovementType.Receipt, quantity, unitCostMinor, dayOffset);

        // A sale carries no cost of its own; it consumes stock at the running average.
        private static StockMovement Sale(decimal quantity, int dayOffset) =>
            NewMovement(StockMovementType.Sale, -quantity, unitCostMinor: 0, dayOffset);

        private static StockMovement NewMovement(
            StockMovementType type,
            decimal quantityChange,
            long unitCostMinor,
            int dayOffset) => new()
            {
                Id = Guid.NewGuid(),
                OrganizationId = OrganizationId,
                ProductId = ProductId,
                Type = type,
                QuantityChange = quantityChange,
                UnitCostMinor = unitCostMinor,
                OccurredAt = Start.AddDays(dayOffset),
                Note = null
            };
    }
}
