using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Metadata;
using Salio.Domain.Entities;
using Salio.Infrastructure;

namespace Salio.Tests
{
    // Guard for locked rule 5: stock changes only through StockMovement rows,
    // only StockPoster adds them, and nothing ever updates or deletes one.
    // No database is used: the EF model is built without connecting, and the
    // rest reads the source files.
    public class StockGuardTests
    {
        private const string StockPosterFile = "Salio.Infrastructure/StockPoster.cs";

        private static readonly Regex StockTable = new(
            @"\bStockMovements\b|\bSet\s*<\s*(Salio\.Domain\.Entities\.)?StockMovement\s*>");

        [Fact]
        public void A_product_stores_money_but_never_a_quantity()
        {
            using SalioDbContext db = SalioModel.NewContext();
            IEntityType product = db.Model.FindEntityType(typeof(Product))
                ?? throw new InvalidOperationException("Product is not in the EF model.");

            // Every number on a product must be an amount of money (…Minor).
            // A quantity column here could be edited directly; quantity on
            // hand must come from adding up stock movements.
            // One named exemption, decided 2026-11-09: ReorderLevel is the
            // level the owner wants to be warned at, a setting typed by a
            // person. It is never stock on hand and nothing adds to it.
            List<string> problems = product.GetProperties()
                .Where(p => IsNumber(p.ClrType) && !p.Name.EndsWith("Minor"))
                .Where(p => p.Name != nameof(Product.ReorderLevel))
                .Select(p => "Product." + p.Name + " (" + p.ClrType.Name + ")")
                .ToList();

            Assert.True(problems.Count == 0,
                "Product holds a number that is not money, which looks like a stored quantity:\n"
                + string.Join("\n", problems));
        }

        [Fact]
        public void No_table_but_StockMovements_stores_a_stock_quantity()
        {
            using SalioDbContext db = SalioModel.NewContext();
            Regex stockName = new("OnHand|InHand|Stock", RegexOptions.IgnoreCase);

            List<string> problems = new();
            foreach (IEntityType entity in db.Model.GetEntityTypes())
            {
                if (entity.ClrType == typeof(StockMovement))
                {
                    continue;
                }
                foreach (IProperty property in entity.GetProperties())
                {
                    if (IsNumber(property.ClrType) && stockName.IsMatch(property.Name))
                    {
                        problems.Add(entity.ClrType.Name + "." + property.Name);
                    }
                }
            }

            Assert.True(problems.Count == 0,
                "A stock quantity is stored outside StockMovements:\n" + string.Join("\n", problems));
        }

        [Fact]
        public void Only_StockPoster_adds_stock_movements()
        {
            List<string> problems = new();

            foreach (SourceCode.SourceFile file in SourceCode.ProductionFiles())
            {
                if (file.RelativePath == StockPosterFile)
                {
                    continue;
                }

                foreach (SourceCode.Write write in SourceCode.WritesTo(file, StockTable))
                {
                    problems.Add(write.Where);
                }
                problems.AddRange(SourceCode.AliasesOf(file, "StockMovement", "StockMovements"));
                problems.AddRange(SourceCode.RawSqlWrites(
                    file,
                    new[] { "StockMovements" },
                    new[] { "INSERT", "UPDATE", "DELETE", "TRUNCATE", "MERGE" }));
            }

            problems.AddRange(SourceCode.UncheckableWrites());

            Assert.True(problems.Count == 0,
                "Only StockPoster may write stock movements. Found:\n" + string.Join("\n", problems));
        }

        [Fact]
        public void Nothing_updates_or_deletes_a_stock_movement()
        {
            List<string> problems = new();

            // StockPoster included: it may add, and nothing more.
            foreach (SourceCode.SourceFile file in SourceCode.ProductionFiles())
            {
                foreach (SourceCode.Write write in SourceCode.WritesTo(file, StockTable))
                {
                    if (SourceCode.ChangeMethods.Contains(write.Method))
                    {
                        problems.Add(write.Where);
                    }
                }
                problems.AddRange(SourceCode.RawSqlWrites(
                    file,
                    new[] { "StockMovements" },
                    new[] { "UPDATE", "DELETE", "TRUNCATE", "MERGE" }));
            }

            problems.AddRange(SourceCode.UncheckableWrites());

            Assert.True(problems.Count == 0,
                "Locked rule 5: stock movements are append-only. Found:\n" + string.Join("\n", problems));
        }

        [Fact]
        public void StockPoster_really_adds_stock_movements()
        {
            // If StockPoster stopped writing (or was renamed), the test above
            // would pass while checking nothing.
            SourceCode.SourceFile poster = SourceCode.ProductionFile(StockPosterFile);

            List<SourceCode.Write> writes = SourceCode.WritesTo(poster, StockTable);

            Assert.True(writes.Count > 0,
                "StockPoster no longer adds stock movements, so these guards check nothing. Update the guard.");
        }

        private static bool IsNumber(Type type)
        {
            Type plain = Nullable.GetUnderlyingType(type) ?? type;
            return plain == typeof(int) || plain == typeof(long) || plain == typeof(short)
                || plain == typeof(byte) || plain == typeof(decimal)
                || plain == typeof(double) || plain == typeof(float);
        }
    }
}
