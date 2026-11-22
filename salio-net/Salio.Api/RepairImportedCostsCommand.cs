using Microsoft.EntityFrameworkCore;
using Salio.Domain.Entities;
using Salio.Domain.Enums;
using Salio.Domain.Services;
using Salio.Infrastructure;
using Salio.Infrastructure.Reports;

namespace Salio.Api;

/// <summary>
/// One-time repair: reverses the opening stock a product import brought into
/// one shop, so the real costs can be entered afterwards. Dry run unless
/// --commit is given.
///
///     dotnet run --project Salio.Api -- repair-imported-costs "Kamau Motor Spares"
///     dotnet run --project Salio.Api -- repair-imported-costs "Kamau Motor Spares" --commit
///
/// On the server:
///
///     docker compose run --rm api repair-imported-costs "Kamau Motor Spares"
///
/// The shop may be named by its exact name or by its id.
///
/// It writes nothing of its own. Each part's opening-stock journal entry is
/// mirrored by JournalReverser (through JournalPoster) and its stock movement
/// opposed (through StockPoster), exactly as an import undo does. Nothing is
/// deleted or updated. Parts stay on the shelf list.
///
/// A part that has sold, or had any other stock movement, since the import
/// cannot be reversed cleanly: the entry can only be mirrored whole, and its
/// units have already left at the imported cost. The command refuses if any
/// such part exists. With --force it reverses the clean parts only, leaves
/// the others exactly as they are, and says what stays wrong.
/// </summary>
public static class RepairImportedCostsCommand
{
    public const string Name = "repair-imported-costs";

    private const string Usage =
        "Usage: dotnet run --project Salio.Api -- repair-imported-costs \"<shop name or id>\" [--commit] [--force]";

    private const string ReversalReason = "imported cost repair";
    private const string ReversalNote = "Reversal of imported cost";

    // One imported part, as found.
    private sealed class Candidate
    {
        public ImportedStock Imported { get; init; } = null!;
        public Product Product { get; init; } = null!;
        public List<JournalLine> EntryLines { get; init; } = new();
        public decimal ImportedQuantity { get; init; }
        public long ImportedUnitCostMinor { get; init; }
        public long ImportedValueMinor { get; init; }
        public decimal SoldQuantity { get; init; }
        public long CostOfSalesMinor { get; init; }
        public int OtherMovements { get; init; }
        public decimal QuantityNow { get; init; }
        public long AverageCostNowMinor { get; init; }

        public bool Clean => OtherMovements == 0;
    }

    // Returns the process exit code: 0 done (or dry run), 1 bad input or
    // failure, 2 refused.
    public static async Task<int> RunAsync(string[] args)
    {
        // args[0] is the command name.
        var words = args.Skip(1).Where(a => !a.StartsWith("--")).ToList();
        var flags = args.Skip(1).Where(a => a.StartsWith("--")).ToList();
        bool commit = flags.Contains("--commit");
        bool force = flags.Contains("--force");

        if (words.Count != 1 || flags.Any(f => f != "--commit" && f != "--force"))
        {
            Console.Error.WriteLine(Usage);
            return 1;
        }

        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.None);

        builder.Services.AddDbContext<SalioDbContext>(options =>
            options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));
        builder.Services.AddScoped<LedgerService>();
        builder.Services.AddScoped<JournalPoster>();
        builder.Services.AddScoped<StockPoster>();
        builder.Services.AddScoped<JournalReverser>();
        builder.Services.AddScoped<StockService>();
        builder.Services.AddScoped<ReportService>();
        builder.Services.AddScoped<ImportedCostFinder>();

        // Built but never run: no web server starts.
        await using var app = builder.Build();
        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<SalioDbContext>();
        var ct = CancellationToken.None;

        // ---- Which shop. Exactly one, or nothing happens. ----
        // The name matches whatever its capitals: "jimkim2" finds "Jimkim2".
        string named = words[0].Trim();
        string namedLower = named.ToLower();
        List<Organization> shops = Guid.TryParse(named, out Guid shopId)
            ? await db.Organizations.AsNoTracking().Where(o => o.Id == shopId).ToListAsync(ct)
            : await db.Organizations.AsNoTracking().Where(o => o.Name.ToLower() == namedLower).ToListAsync(ct);

        if (shops.Count == 0)
        {
            Console.Error.WriteLine("No shop is called \"" + named + "\". Nothing was done.");

            // Say what does exist, so the next try is not a guess.
            var existing = await db.Organizations.AsNoTracking().OrderBy(o => o.Name).ToListAsync(ct);
            Console.Error.WriteLine(existing.Count == 0 ? "There are no shops." : "The shops are:");

            foreach (var s in existing)
            {
                Console.Error.WriteLine("  " + s.Name + "  (" + s.Id + ")");
            }

            return 1;
        }

        if (shops.Count > 1)
        {
            Console.Error.WriteLine(shops.Count + " shops are called \"" + named + "\". Name it by id instead: "
                + string.Join(", ", shops.Select(s => s.Id)) + ". Nothing was done.");
            return 1;
        }

        Organization shop = shops[0];
        Guid org = shop.Id;
        DateOnly today = JournalReverser.TodayInNairobi();

        Console.WriteLine("Shop:    " + shop.Name + " (" + org + ")");
        Console.WriteLine("Mode:    " + (commit ? "COMMIT" : "DRY RUN, nothing will be written")
            + (force ? ", --force" : ""));
        Console.WriteLine("Date of any reversal: " + today.ToString("yyyy-MM-dd"));
        Console.WriteLine();

        // ---- What the imports brought in. ----
        var finder = services.GetRequiredService<ImportedCostFinder>();
        var imported = await finder.FindAsync(org, ct);

        var openingStock = imported
            .Where(i => i.Movements.Count > 0
                && i.Movements.All(m => m.Type == StockMovementType.Adjustment)
                && i.JournalEntryId is not null)
            .ToList();
        int purchaseImports = imported.Count(i => i.Movements.Any(m => m.Type == StockMovementType.Receipt));
        int alreadyReversed = openingStock.Count(i => i.Reversed);
        var toConsider = openingStock.Where(i => !i.Reversed).ToList();

        Console.WriteLine("Parts created by an import:          " + imported.Count);
        Console.WriteLine("  with imported opening stock:       " + openingStock.Count);
        Console.WriteLine("  of those, already reversed:        " + alreadyReversed);
        Console.WriteLine("  brought in as an import purchase:  " + purchaseImports
            + (purchaseImports > 0 ? "  (NOT handled here: one receipt for the whole file)" : ""));
        Console.WriteLine();

        if (toConsider.Count == 0)
        {
            Console.WriteLine("Nothing to reverse. Nothing was written.");
            return 0;
        }

        var candidates = await LoadCandidatesAsync(db, services.GetRequiredService<StockService>(), org, toConsider, ct);
        var clean = candidates.Where(c => c.Clean).ToList();
        var blended = candidates.Where(c => !c.Clean).ToList();

        var accounts = await db.Accounts.AsNoTracking()
            .Where(a => a.OrganizationId == org)
            .ToDictionaryAsync(a => a.Id, ct);

        PrintHowWrong(candidates);
        PrintParts("Can be reversed cleanly (no sale, no other stock movement since the import):", clean, accounts);
        PrintBlended(blended);

        if (blended.Count > 0 && !force)
        {
            Console.WriteLine("REFUSED: " + blended.Count + " imported part(s) have sold or moved since the import.");
            Console.WriteLine("A clean reversal of those is not possible (see above). Nothing was written.");
            Console.WriteLine("Run again with --force to reverse only the " + clean.Count
                + " clean part(s) and leave the rest exactly as they are.");
            return 2;
        }

        if (clean.Count == 0)
        {
            Console.WriteLine("No part can be reversed cleanly. Nothing was written.");
            return 0;
        }

        // ---- The books, before and after. ----
        var reports = services.GetRequiredService<ReportService>();
        TrialBalanceReport before = await reports.GetTrialBalanceAsync(org, today, ct);

        // Each reversal swaps every line of the original entry, same account,
        // same amount. Shown here as what that does to each account's totals.
        var change = new Dictionary<Guid, (long Debit, long Credit)>();
        foreach (var line in clean.SelectMany(c => c.EntryLines))
        {
            change.TryGetValue(line.AccountId, out var sum);
            change[line.AccountId] = (sum.Debit + line.CreditMinor, sum.Credit + line.DebitMinor);
        }

        if (!commit)
        {
            PrintTrialBalance(before, change, accounts, "projected");
            Console.WriteLine("DRY RUN: nothing was written. Run again with --commit to post "
                + clean.Count + " reversal(s).");
            return 0;
        }

        // ---- Commit: through the existing reverser only, in one transaction. ----
        var reverser = services.GetRequiredService<JournalReverser>();
        using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            try
            {
                foreach (var candidate in clean)
                {
                    await reverser.ReverseAsync(org, candidate.Imported.JournalEntryId!.Value, ReversalReason, today, ct);
                    await reverser.ReverseStockAsync(org, candidate.Imported.Movements, ReversalNote, today, ct);
                }

                await transaction.CommitAsync(ct);
            }
            catch (InvalidOperationException ex)
            {
                await transaction.RollbackAsync(ct);
                Console.Error.WriteLine("FAILED, nothing was written: " + ex.Message);
                return 1;
            }
        }

        db.ChangeTracker.Clear();
        TrialBalanceReport after = await reports.GetTrialBalanceAsync(org, today, ct);
        PrintTrialBalance(before, null, accounts, "after", after);
        Console.WriteLine("DONE: " + clean.Count + " part(s) reversed.");
        Console.WriteLine("Enter each one's real cost and the count on the shelf on the Opening stock screen,");
        Console.WriteLine("dated " + today.ToString("yyyy-MM-dd") + " or later, before they are sold again.");
        Console.WriteLine("Opening stock is allowed again for these parts only, once each.");
        return 0;
    }

    private static async Task<List<Candidate>> LoadCandidatesAsync(
        SalioDbContext db, StockService stockService, Guid org, List<ImportedStock> toConsider, CancellationToken ct)
    {
        var productIds = toConsider.Select(i => i.ProductId).Distinct().ToList();
        var entryIds = toConsider.Select(i => i.JournalEntryId!.Value).ToList();

        var products = await db.Products.AsNoTracking()
            .Where(p => p.OrganizationId == org && productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);
        var movements = await db.StockMovements.AsNoTracking()
            .Where(m => m.OrganizationId == org && productIds.Contains(m.ProductId))
            .ToListAsync(ct);
        var entryLines = await db.JournalLines.AsNoTracking()
            .Where(l => l.OrganizationId == org && entryIds.Contains(l.JournalEntryId))
            .ToListAsync(ct);
        var soldLines = await db.SaleLines.AsNoTracking()
            .Where(l => l.OrganizationId == org && productIds.Contains(l.ProductId))
            .Join(db.Sales.Where(s => s.Status != SaleStatus.Draft), l => l.SaleId, s => s.Id,
                (l, s) => new { l.ProductId, l.Quantity, l.UnitCostMinor })
            .ToListAsync(ct);

        var result = new List<Candidate>();

        foreach (var imported in toConsider)
        {
            var ownIds = imported.Movements.Select(m => m.Id).ToHashSet();
            var all = movements.Where(m => m.ProductId == imported.ProductId).ToList();
            var lines = entryLines.Where(l => l.JournalEntryId == imported.JournalEntryId).ToList();

            // The same rounding SaleService used when it booked each line.
            long costOfSales = 0;
            foreach (var sold in soldLines.Where(s => s.ProductId == imported.ProductId))
            {
                costOfSales += (long)Math.Round(sold.Quantity * sold.UnitCostMinor, MidpointRounding.AwayFromZero);
            }

            result.Add(new Candidate
            {
                Imported = imported,
                Product = products[imported.ProductId],
                EntryLines = lines,
                ImportedQuantity = imported.Movements.Sum(m => m.QuantityChange),
                ImportedUnitCostMinor = imported.Movements[0].UnitCostMinor,
                ImportedValueMinor = lines.Sum(l => l.DebitMinor),
                SoldQuantity = -all.Where(m => m.Type == StockMovementType.Sale).Sum(m => m.QuantityChange),
                CostOfSalesMinor = costOfSales,
                OtherMovements = all.Count(m => !ownIds.Contains(m.Id)),
                QuantityNow = stockService.CurrentQuantity(all),
                AverageCostNowMinor = stockService.CalculateWeightedAverageCost(all)
            });
        }

        return result.OrderBy(c => c.Product.Sku).ToList();
    }

    // How far the books rest on imported costs today.
    private static void PrintHowWrong(List<Candidate> candidates)
    {
        long importedValue = candidates.Sum(c => c.ImportedValueMinor);
        long costOfSales = candidates.Sum(c => c.CostOfSalesMinor);
        long valueNow = candidates.Sum(c => c.QuantityNow > 0
            ? (long)Math.Round(c.QuantityNow * c.AverageCostNowMinor, MidpointRounding.AwayFromZero)
            : 0);

        Console.WriteLine("HOW FAR THE BOOKS REST ON IMPORTED COSTS (parts not yet reversed: " + candidates.Count + ")");
        Console.WriteLine("  Opening stock booked at imported costs (Dr 1200 / Cr 3000):  KES " + Kes(importedValue));
        Console.WriteLine("  Cost of sales booked on these parts since (Dr 5000 / Cr 1200): KES " + Kes(costOfSales));
        Console.WriteLine("  Stock of these parts still valued now (qty x average):        KES " + Kes(valueNow));
        Console.WriteLine("  (The last two include any real stock received on the same parts.)");
        Console.WriteLine();
    }

    private static void PrintParts(string heading, List<Candidate> parts, Dictionary<Guid, Account> accounts)
    {
        Console.WriteLine(heading + " " + parts.Count);

        foreach (var c in parts)
        {
            Console.WriteLine("  " + c.Product.Sku + "  " + c.Product.Name);
            Console.WriteLine("    reverse " + Qty(c.ImportedQuantity) + " @ KES " + Kes(c.ImportedUnitCostMinor)
                + " = KES " + Kes(c.ImportedValueMinor));

            foreach (var line in c.EntryLines)
            {
                // The mirror: the original's credit becomes a debit, and so on.
                string account = AccountName(accounts, line.AccountId);
                if (line.CreditMinor > 0)
                {
                    Console.WriteLine("    Dr " + account + "  " + Kes(line.CreditMinor));
                }
                if (line.DebitMinor > 0)
                {
                    Console.WriteLine("    Cr " + account + "  " + Kes(line.DebitMinor));
                }
            }
        }

        Console.WriteLine();
    }

    private static void PrintBlended(List<Candidate> blended)
    {
        if (blended.Count == 0)
        {
            return;
        }

        Console.WriteLine("Sold or moved since the import, so NOT reversible cleanly: " + blended.Count);

        foreach (var c in blended)
        {
            Console.WriteLine("  " + c.Product.Sku + "  " + c.Product.Name);
            Console.WriteLine("    imported " + Qty(c.ImportedQuantity) + " @ KES " + Kes(c.ImportedUnitCostMinor)
                + "; sold " + Qty(c.SoldQuantity) + "; " + c.OtherMovements + " other movement(s); now "
                + Qty(c.QuantityNow) + " on the books @ KES " + Kes(c.AverageCostNowMinor));
            Console.WriteLine("    Left wrong if not reversed: cost of sales KES " + Kes(c.CostOfSalesMinor)
                + " was booked from the imported cost and stays; the "
                + Qty(c.QuantityNow) + " still on the books stay valued from it; the part stays 'cost not confirmed'.");
            Console.WriteLine("    Why not reverse it anyway: the entry can only be mirrored whole. That takes all "
                + Qty(c.ImportedQuantity) + " out of stock and KES " + Kes(c.ImportedValueMinor)
                + " out of 1200 Inventory, though " + Qty(c.SoldQuantity)
                + " already left at that cost: stock and 1200 would go below zero by what was sold.");
        }

        Console.WriteLine();
    }

    private static void PrintTrialBalance(
        TrialBalanceReport before,
        Dictionary<Guid, (long Debit, long Credit)>? change,
        Dictionary<Guid, Account> accounts,
        string afterLabel,
        TrialBalanceReport? after = null)
    {
        Console.WriteLine("TRIAL BALANCE as of " + before.AsOf.ToString("yyyy-MM-dd") + " (net per account)");
        Console.WriteLine("  " + "Account".PadRight(36) + "before".PadLeft(18) + afterLabel.PadLeft(18));

        var accountIds = before.Rows.Select(r => r.AccountId).ToList();
        accountIds.AddRange((change?.Keys ?? Enumerable.Empty<Guid>()).Where(id => !accountIds.Contains(id)));
        accountIds.AddRange((after?.Rows.Select(r => r.AccountId) ?? Enumerable.Empty<Guid>()).Where(id => !accountIds.Contains(id)));

        long debitAfter = 0;
        long creditAfter = 0;

        foreach (Guid id in accountIds)
        {
            var row = before.Rows.FirstOrDefault(r => r.AccountId == id);
            long d0 = row?.TotalDebitMinor ?? 0;
            long c0 = row?.TotalCreditMinor ?? 0;

            long d1;
            long c1;
            if (after is not null)
            {
                var a = after.Rows.FirstOrDefault(r => r.AccountId == id);
                d1 = a?.TotalDebitMinor ?? 0;
                c1 = a?.TotalCreditMinor ?? 0;
            }
            else
            {
                change!.TryGetValue(id, out var delta);
                d1 = d0 + delta.Debit;
                c1 = c0 + delta.Credit;
            }

            debitAfter += d1;
            creditAfter += c1;

            string mark = (d0 - c0) != (d1 - c1) ? "  <" : "";
            Console.WriteLine("  " + AccountName(accounts, id).PadRight(36) + Net(d0 - c0).PadLeft(18)
                + Net(d1 - c1).PadLeft(18) + mark);
        }

        Console.WriteLine("  " + "Totals (debits / credits)".PadRight(36)
            + (Kes(before.TotalDebitMinor) + "/" + Kes(before.TotalCreditMinor)).PadLeft(18)
            + (Kes(debitAfter) + "/" + Kes(creditAfter)).PadLeft(18));
        Console.WriteLine();
    }

    private static string AccountName(Dictionary<Guid, Account> accounts, Guid id)
    {
        return accounts.TryGetValue(id, out var account) ? account.Code + " " + account.Name : id.ToString();
    }

    private static string Net(long netMinor)
    {
        return netMinor >= 0 ? "Dr " + Kes(netMinor) : "Cr " + Kes(-netMinor);
    }

    private static string Kes(long minor)
    {
        long whole = minor / 100;
        long cents = Math.Abs(minor % 100);
        string sign = minor < 0 && whole == 0 ? "-" : "";
        return sign + whole.ToString("N0") + "." + cents.ToString("00");
    }

    private static string Qty(decimal quantity)
    {
        return quantity.ToString("0.###");
    }
}
