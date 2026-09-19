using Microsoft.EntityFrameworkCore;
using Salio.Domain.Entities;
using Salio.Domain.Enums;

namespace Salio.Infrastructure
{
    // DEVELOPMENT ONLY. A small motorcycle spares shelf so the till has
    // something realistic to sell. Prices are typical Nairobi retail for
    // Bajaj Boxer parts, in cents: 55000 is KES 550.00. Not supplier quotes.
    public static class DemoProductSeeder
    {
        // One row of the demo shelf. A record is just a short way to write a
        // class whose values are set once, in the order listed here.
        private record DemoProduct(
            string Sku, string Name, string BinLocation,
            long SellPriceMinor, long UnitCostMinor, int OpeningQuantity);

        private static readonly List<DemoProduct> Shelf = new()
        {
            new("BRK-PAD-BX150", "Brake Pads, Front Disc - Boxer 150", "A1", 55000, 38000, 14),
            new("CHN-SPR-428H", "Chain and Sprocket Set 428H - Boxer 150", "B3", 280000, 205000, 6),
            new("SPK-NGK-C7HSA", "Spark Plug NGK C7HSA", "A2", 35000, 24000, 40),
            new("CLT-PLT-BX150", "Clutch Plate Set - Boxer 150", "B1", 120000, 85000, 9),
            new("PST-KIT-BX150", "Piston Kit, Standard - Boxer 150", "C2", 350000, 260000, 4),
            new("CDI-BX150", "CDI Unit - Boxer 150", "C1", 150000, 105000, 0),
            new("TYR-300-18", "Rear Tyre 3.00-18", "D1", 380000, 290000, 5),
            new("TUB-300-18", "Inner Tube 3.00-18", "D2", 65000, 43000, 18),
            new("MIR-PAIR-UNI", "Side Mirrors, Pair - Universal", "A3", 45000, 28000, 11),
            new("CBL-CLT-BX150", "Clutch Cable - Boxer 150", "B2", 30000, 19000, 16),
            new("BLB-HL-12V35", "Headlight Bulb 12V 35/35W", "A4", 15000, 9000, 25),
            new("BLT-M10-40", "Bolt M10 x 40mm", "C3", 3000, 1500, 120)
        };

        public static async Task SeedAsync(
            SalioDbContext db,
            StockPoster stockPoster,
            JournalPoster journalPoster,
            Guid organizationId,
            CancellationToken cancellationToken)
        {
            // Checked per SKU, so products made by hand are left alone and a
            // restart never creates duplicates.
            var existingSkus = await db.Products
                .Where(p => p.OrganizationId == organizationId)
                .Select(p => p.Sku)
                .ToListAsync(cancellationToken);

            var products = new List<Product>();
            var movements = new List<StockMovement>();
            long openingValueMinor = 0;

            foreach (var demo in Shelf)
            {
                if (existingSkus.Contains(demo.Sku))
                {
                    continue;
                }

                var product = new Product
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = organizationId,
                    Sku = demo.Sku,
                    Name = demo.Name,
                    SellPriceMinor = demo.SellPriceMinor,
                    BinLocation = demo.BinLocation,
                    IsActive = true
                };
                products.Add(product);

                // Out of stock on purpose: no movement at all, so quantity is 0.
                if (demo.OpeningQuantity == 0)
                {
                    continue;
                }

                movements.Add(new StockMovement
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = organizationId,
                    ProductId = product.Id,
                    Type = StockMovementType.Receipt,
                    QuantityChange = demo.OpeningQuantity,
                    UnitCostMinor = demo.UnitCostMinor,
                    OccurredAt = DateTimeOffset.UtcNow,
                    Note = "Opening stock (demo seed)"
                });

                openingValueMinor = openingValueMinor + (demo.OpeningQuantity * demo.UnitCostMinor);
            }

            if (products.Count == 0)
            {
                return;
            }

            // Products, stock and books land together or not at all. Otherwise a
            // half-finished seed would be skipped on the next run and never fixed.
            using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            db.Products.AddRange(products);
            await db.SaveChangesAsync(cancellationToken);

            if (movements.Count > 0)
            {
                await stockPoster.PostAsync(movements, cancellationToken);

                // Stock on the shelf is worth money, so the books must show it.
                // Without this, the first sale would push Inventory below zero.
                var entry = OpeningStockEntry(organizationId);
                var lines = await OpeningStockLinesAsync(db, organizationId, openingValueMinor, cancellationToken);
                await journalPoster.PostAsync(entry, lines, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }

        private static JournalEntry OpeningStockEntry(Guid organizationId) => new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            EntryDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Description = "Opening stock (demo seed)",
            SourceType = SourceType.StockAdjustment,
            SourceId = null,
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Debit Inventory, credit Owner's Equity: the owner brought the stock in.
        private static async Task<List<JournalLine>> OpeningStockLinesAsync(
            SalioDbContext db,
            Guid organizationId,
            long openingValueMinor,
            CancellationToken cancellationToken)
        {
            Guid inventoryAccountId = await AccountIdAsync(db, organizationId, "1200", cancellationToken);
            Guid equityAccountId = await AccountIdAsync(db, organizationId, "3000", cancellationToken);

            return new List<JournalLine>
            {
                NewLine(organizationId, inventoryAccountId, openingValueMinor, 0),
                NewLine(organizationId, equityAccountId, 0, openingValueMinor)
            };
        }

        private static async Task<Guid> AccountIdAsync(
            SalioDbContext db, Guid organizationId, string code, CancellationToken cancellationToken)
        {
            return await db.Accounts
                .Where(a => a.OrganizationId == organizationId && a.Code == code)
                .Select(a => a.Id)
                .SingleAsync(cancellationToken);
        }

        private static JournalLine NewLine(Guid organizationId, Guid accountId, long debitMinor, long creditMinor) => new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            AccountId = accountId,
            DebitMinor = debitMinor,
            CreditMinor = creditMinor,
            Currency = "KES"
        };
    }
}
