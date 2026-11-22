using System.Text.RegularExpressions;
using Salio.Domain.Entities;
using Salio.Domain.Enums;
using Salio.Domain.Services;
using Xunit;

namespace Salio.Tests;

// The one exception to "no opening stock after the first sale": a part
// whose imported opening stock was reversed may have it entered again,
// once, dated no earlier than the reversal. Nothing else gets through.
public class OpeningStockReentryRulesTests
{
    private static readonly Guid Part = Guid.NewGuid();
    private static readonly DateOnly ReversedOn = new(2026, 11, 20);

    private static StockMovement Movement(StockMovementType type, string note, decimal quantity)
    {
        return new StockMovement
        {
            Id = Guid.NewGuid(),
            ProductId = Part,
            Type = type,
            QuantityChange = quantity,
            UnitCostMinor = 31500,
            OccurredAt = DateTimeOffset.UtcNow,
            Note = note
        };
    }

    private static StockMovement ImportedOpening() =>
        Movement(StockMovementType.Adjustment, "Opening stock 1a2b3c4d", 10);

    private static StockMovement ItsReversal() =>
        Movement(StockMovementType.Adjustment, "Reversal of imported cost", -10);

    [Fact]
    public void A_part_whose_imported_opening_stock_was_reversed_may_be_entered_again()
    {
        var imported = ImportedOpening();

        Assert.True(OpeningStockReentryRules.MayReenter(
            new[] { imported, ItsReversal() }, new HashSet<Guid> { imported.Id }));
    }

    [Fact]
    public void Only_once_opening_stock_entered_since_closes_it_again()
    {
        var imported = ImportedOpening();
        var reentered = Movement(StockMovementType.Adjustment, "Opening stock 9f8e7d6c", 7);

        Assert.False(OpeningStockReentryRules.MayReenter(
            new[] { imported, ItsReversal(), reentered }, new HashSet<Guid> { imported.Id }));
    }

    [Fact]
    public void An_import_not_reversed_does_not_qualify()
    {
        Assert.False(OpeningStockReentryRules.MayReenter(new[] { ImportedOpening() }, new HashSet<Guid>()));
    }

    [Fact]
    public void A_part_never_imported_does_not_qualify()
    {
        var typedIn = Movement(StockMovementType.Adjustment, "Opening stock 5e6f7a8b", 4);
        var received = Movement(StockMovementType.Receipt, "Stock receipt 0a1b2c3d", 6);

        Assert.False(OpeningStockReentryRules.MayReenter(new[] { typedIn, received }, new HashSet<Guid>()));
        Assert.False(OpeningStockReentryRules.MayReenter(Array.Empty<StockMovement>(), new HashSet<Guid>()));
    }

    [Fact]
    public void Every_part_on_the_list_must_qualify()
    {
        var eligible = new Dictionary<Guid, DateOnly> { [Part] = ReversedOn };

        Assert.Equal(OpeningStockReentryDecision.Allowed,
            OpeningStockReentryRules.Decide(new[] { Part }, eligible, ReversedOn));
        Assert.Equal(OpeningStockReentryDecision.NotEligible,
            OpeningStockReentryRules.Decide(new[] { Part, Guid.NewGuid() }, eligible, ReversedOn));
        Assert.Equal(OpeningStockReentryDecision.NotEligible,
            OpeningStockReentryRules.Decide(Array.Empty<Guid>(), eligible, ReversedOn));
    }

    [Fact]
    public void It_may_not_be_dated_before_the_reversal()
    {
        var eligible = new Dictionary<Guid, DateOnly> { [Part] = ReversedOn };

        Assert.Equal(OpeningStockReentryDecision.TooEarly,
            OpeningStockReentryRules.Decide(new[] { Part }, eligible, ReversedOn.AddDays(-1)));
        Assert.Equal(OpeningStockReentryDecision.Allowed,
            OpeningStockReentryRules.Decide(new[] { Part }, eligible, ReversedOn.AddDays(3)));
        Assert.Equal(ReversedOn, OpeningStockReentryRules.EarliestDate(new[] { Part }, eligible));
    }

    [Fact]
    public void The_exception_lives_inside_the_sale_check_and_nowhere_else()
    {
        // The writer still asks "has this shop sold?" first. The exception is
        // only consulted inside that check, before anything is posted, and
        // nothing outside OpeningStockWriter can open it.
        string code = SourceCode.ProductionFile("Salio.Infrastructure/OpeningStockWriter.cs").Code;

        Match check = Regex.Match(code, @"\bif\s*\(\s*await\s+ShopHasSoldAsync\s*\(");
        Match decide = Regex.Match(code, @"OpeningStockReentryRules\s*\.\s*Decide\s*\(");
        Match firstPost = Regex.Match(code, @"_journalPoster\s*\.\s*PostAsync\s*\(");

        Assert.True(check.Success && decide.Success && firstPost.Success);
        Assert.True(check.Index < decide.Index && decide.Index < firstPost.Index);

        foreach (SourceCode.SourceFile file in SourceCode.ProductionFiles())
        {
            if (file.RelativePath is "Salio.Infrastructure/OpeningStockWriter.cs"
                or "Salio.Domain/Services/OpeningStockReentryRules.cs")
            {
                continue;
            }

            Assert.DoesNotMatch(@"OpeningStockReentryRules\s*\.\s*Decide\b", file.Code);
        }
    }
}
