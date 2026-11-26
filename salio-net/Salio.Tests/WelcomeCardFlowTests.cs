using Salio.Api.Controllers;
using Salio.Api.Dtos;
using Salio.Domain.Entities;
using Salio.Domain.Enums;
using Salio.Domain.Services;

namespace Salio.Tests;

// The Owner's "what changed" card, end to end through the controller's own
// BuildCard: deliveries in, card out, Hide, another delivery.
//
// The repo has no API tests with a database (none exists to test against,
// and these tests never connect to one), so this drives everything the GET
// endpoint does after its reads, with the rows it would have read. Amounts
// in minor units: KES 800 is 80000.
public class WelcomeCardFlowTests
{
    private static readonly Guid Shop = Guid.NewGuid();
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly HashSet<Guid> NoImports = new();
    private static readonly Dictionary<Guid, string> NoNames = new();

    private static readonly Product Oil = new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = Shop,
        Sku = "OIL-1",
        Name = "Engine Oil 20W-50 1L",
        SellPriceMinor = 150000,
        IsActive = true
    };

    private static StockMovement Delivery(long unitCostMinor, DateTimeOffset at) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = Shop,
        ProductId = Oil.Id,
        Type = StockMovementType.Receipt,
        QuantityChange = 2,
        UnitCostMinor = unitCostMinor,
        OccurredAt = at,
        Note = "Stock receipt 9f8e7d6c"
    };

    private static WelcomeCardResponse? Card(
        MembershipRole role, Membership membership, List<StockMovement> movements, DateTimeOffset now)
    {
        DateTimeOffset since = WelcomeCardRules.CardSince(membership.ChangesSeenUntil, now);

        return WelcomeCardController.BuildCard(
            role, OwnerId, since, new List<Product> { Oil }, movements, NoImports,
            new List<PriceChange>(), NoNames);
    }

    [Fact]
    public void A_cost_rise_shows_until_hidden_and_comes_back_on_the_next_rise()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var owner = new Membership
        {
            Id = Guid.NewGuid(),
            OrganizationId = Shop,
            AppUserId = OwnerId,
            Role = MembershipRole.Owner
        };
        var person = new AppUser { Id = OwnerId, FullName = "Charles Owner" };

        // 1. Stocked in at 800, then again at 1,000.
        var movements = new List<StockMovement>
        {
            Delivery(80000, now.AddDays(-3)),
            Delivery(100000, now.AddDays(-1))
        };

        // 2. One "cost went up" row: 800 -> 1,000, sells at 1,500, kept 700
        //    and now keeps 500. Never hidden yet, so the last 7 days count.
        WelcomeCardResponse? card = Card(MembershipRole.Owner, owner, movements, now);
        Assert.NotNull(card);
        WelcomeCardRow row = Assert.Single(card.Rows);
        Assert.Equal(1, card.Total);
        Assert.Equal("costUp", row.Kind);
        Assert.Equal(Oil.Id, row.ProductId);
        Assert.Equal(80000, row.WasMinor);
        Assert.Equal(100000, row.NowMinor);
        Assert.Equal(150000, row.AskingMinor);
        Assert.Equal(70000, row.KeptWasMinor);
        Assert.Equal(50000, row.KeptNowMinor);

        // 3. A Cashier gets no card at all.
        Assert.Null(Card(MembershipRole.Cashier, owner, movements, now));

        // 4. A PIN unlock (or a sign-in) moves the sign-in times, never
        //    ChangesSeenUntil. The card is unchanged.
        person.PreviousSignedInAt = person.LastSignedInAt;
        person.LastSignedInAt = now.AddMinutes(5);
        WelcomeCardResponse? afterUnlock = Card(MembershipRole.Owner, owner, movements, now.AddMinutes(5));
        Assert.NotNull(afterUnlock);
        Assert.Single(afterUnlock.Rows);

        // 5. Hide sets ChangesSeenUntil to now, as the endpoint does: the
        //    card empties.
        DateTimeOffset hiddenAt = now.AddMinutes(10);
        owner.ChangesSeenUntil = hiddenAt;
        WelcomeCardResponse? afterHide = Card(MembershipRole.Owner, owner, movements, hiddenAt);
        Assert.NotNull(afterHide);
        Assert.Empty(afterHide.Rows);

        //    A third delivery at 1,100 brings it back as 1,000 -> 1,100.
        movements.Add(Delivery(110000, hiddenAt.AddHours(1)));
        WelcomeCardResponse? afterThird = Card(MembershipRole.Owner, owner, movements, hiddenAt.AddHours(2));
        Assert.NotNull(afterThird);
        WelcomeCardRow back = Assert.Single(afterThird.Rows);
        Assert.Equal(100000, back.WasMinor);
        Assert.Equal(110000, back.NowMinor);
        Assert.Equal(40000, back.KeptNowMinor);
    }

    [Fact]
    public void Every_unseen_change_is_returned_not_only_five()
    {
        // The bell's sheet lists them all.
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var products = new List<Product>();
        var movements = new List<StockMovement>();

        for (int i = 0; i < 7; i++)
        {
            var part = new Product { Id = Guid.NewGuid(), OrganizationId = Shop, Sku = "P" + i, Name = "Part " + i, SellPriceMinor = 150000 };
            products.Add(part);
            foreach (var (cost, daysAgo) in new[] { (80000L, 3), (100000L, 1) })
            {
                movements.Add(new StockMovement
                {
                    Id = Guid.NewGuid(), OrganizationId = Shop, ProductId = part.Id,
                    Type = StockMovementType.Receipt, QuantityChange = 2, UnitCostMinor = cost,
                    OccurredAt = now.AddDays(-daysAgo).AddMinutes(i), Note = "Stock receipt 9f8e7d6c"
                });
            }
        }

        WelcomeCardResponse? card = WelcomeCardController.BuildCard(
            MembershipRole.Owner, OwnerId, WelcomeCardRules.CardSince(null, now),
            products, movements, NoImports, new List<PriceChange>(), NoNames);

        Assert.NotNull(card);
        Assert.Equal(7, card.Total);
        Assert.Equal(7, card.Rows.Count);
    }

    [Fact]
    public void Never_hidden_means_the_last_seven_days()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        Assert.Equal(now.AddDays(-7), WelcomeCardRules.CardSince(null, now));
        Assert.Equal(now.AddHours(-1), WelcomeCardRules.CardSince(now.AddHours(-1), now));
    }

    [Fact]
    public void Only_hide_moves_changes_seen_until()
    {
        // Sign-in and PIN unlock live in SignInService and AuthController.
        // Neither may name the column: only the card's Hide writes it.
        var allowed = new[]
        {
            "Salio.Domain/Entities/Membership.cs",
            "Salio.Api/Controllers/WelcomeCardController.cs"
        };

        var offenders = SourceCode.ProductionFiles()
            .Where(f => !f.RelativePath.Contains("/Migrations/"))
            .Where(f => f.Code.Contains("ChangesSeenUntil"))
            .Select(f => f.RelativePath)
            .Where(p => !allowed.Contains(p))
            .ToList();

        Assert.Empty(offenders);
    }
}
