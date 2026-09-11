using Microsoft.EntityFrameworkCore;
using Salio.Domain.Entities;
using Salio.Domain.Enums;
using Salio.Domain.Services;
using Salio.Infrastructure.Reports;

namespace Salio.Infrastructure;

/// <summary>
/// Reports are derived on read. Nothing in here is stored: every figure is
/// summed from journal lines at the moment it is asked for.
/// </summary>
public class ReportService
{
    private readonly SalioDbContext _db;
    private readonly StockService _stockService;

    public ReportService(SalioDbContext db, StockService stockService)
    {
        _db = db;
        _stockService = stockService;
    }

    public async Task<TrialBalanceReport> GetTrialBalanceAsync(
        Guid organizationId,
        DateOnly asOf,
        CancellationToken cancellationToken)
    {
        var rows = await (
            from line in _db.JournalLines.AsNoTracking()
            join entry in _db.JournalEntries.AsNoTracking()
                on line.JournalEntryId equals entry.Id
            join account in _db.Accounts.AsNoTracking()
                on line.AccountId equals account.Id
            where line.OrganizationId == organizationId
                && entry.OrganizationId == organizationId
                && account.OrganizationId == organizationId
                && entry.EntryDate <= asOf
            group line by new
            {
                account.Id,
                account.Code,
                account.Name,
                account.AccountClass
            }
            into grouped
            orderby grouped.Key.Code
            select new TrialBalanceRow
            {
                AccountId = grouped.Key.Id,
                Code = grouped.Key.Code,
                Name = grouped.Key.Name,
                AccountClass = grouped.Key.AccountClass,
                TotalDebitMinor = grouped.Sum(l => l.DebitMinor),
                TotalCreditMinor = grouped.Sum(l => l.CreditMinor)
            })
            .ToListAsync(cancellationToken);

        long totalDebitMinor = 0;
        long totalCreditMinor = 0;

        foreach (var row in rows)
        {
            totalDebitMinor = totalDebitMinor + row.TotalDebitMinor;
            totalCreditMinor = totalCreditMinor + row.TotalCreditMinor;
        }

        return new TrialBalanceReport
        {
            AsOf = asOf,
            Rows = rows,
            TotalDebitMinor = totalDebitMinor,
            TotalCreditMinor = totalCreditMinor
        };
    }

    public async Task<ProfitAndLossReport> GetProfitAndLossAsync(
        Guid organizationId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        // 'from' is a LINQ query keyword, so it cannot be read as a variable
        // inside the query below. Copy both dates into ordinary locals first.
        DateOnly fromDate = from;
        DateOnly toDate = to;

        // Same shape as the trial balance, but narrowed to a date range and to
        // the two classes that make up trading: what was sold, what it cost.
        var totals = await (
            from line in _db.JournalLines.AsNoTracking()
            join entry in _db.JournalEntries.AsNoTracking()
                on line.JournalEntryId equals entry.Id
            join account in _db.Accounts.AsNoTracking()
                on line.AccountId equals account.Id
            where line.OrganizationId == organizationId
                && entry.OrganizationId == organizationId
                && account.OrganizationId == organizationId
                && entry.EntryDate >= fromDate
                && entry.EntryDate <= toDate
                && (account.AccountClass == AccountClass.Income
                    || account.AccountClass == AccountClass.Expense)
            group line by new
            {
                account.Id,
                account.Code,
                account.Name,
                account.AccountClass
            }
            into grouped
            orderby grouped.Key.Code
            select new
            {
                grouped.Key.Id,
                grouped.Key.Code,
                grouped.Key.Name,
                grouped.Key.AccountClass,
                DebitMinor = grouped.Sum(l => l.DebitMinor),
                CreditMinor = grouped.Sum(l => l.CreditMinor)
            })
            .ToListAsync(cancellationToken);

        var revenueRows = new List<ProfitAndLossRow>();
        var costOfSalesRows = new List<ProfitAndLossRow>();

        long revenueTotalMinor = 0;
        long costOfSalesTotalMinor = 0;

        foreach (var total in totals)
        {
            if (total.AccountClass == AccountClass.Income)
            {
                // Income sits on the credit side, so credits are the earnings
                // and debits are what came back off them - a discount or a
                // refund reduces revenue rather than becoming a cost.
                long amountMinor = total.CreditMinor - total.DebitMinor;

                revenueRows.Add(NewRow(total.Id, total.Code, total.Name, total.AccountClass, amountMinor));
                revenueTotalMinor = revenueTotalMinor + amountMinor;
            }
            else
            {
                // Expense sits on the debit side, so the sign is the other way.
                long amountMinor = total.DebitMinor - total.CreditMinor;

                costOfSalesRows.Add(NewRow(total.Id, total.Code, total.Name, total.AccountClass, amountMinor));
                costOfSalesTotalMinor = costOfSalesTotalMinor + amountMinor;
            }
        }

        return new ProfitAndLossReport
        {
            From = from,
            To = to,
            RevenueRows = revenueRows,
            CostOfSalesRows = costOfSalesRows,
            RevenueTotalMinor = revenueTotalMinor,
            CostOfSalesTotalMinor = costOfSalesTotalMinor,
            GrossProfitMinor = revenueTotalMinor - costOfSalesTotalMinor
        };
    }

    private static ProfitAndLossRow NewRow(
        Guid accountId,
        string code,
        string name,
        AccountClass accountClass,
        long amountMinor)
    {
        return new ProfitAndLossRow
        {
            AccountId = accountId,
            Code = code,
            Name = name,
            AccountClass = accountClass,
            AmountMinor = amountMinor
        };
    }

    public async Task<StockValuationReport> GetStockValuationAsync(
        Guid organizationId,
        DateOnly asOf,
        CancellationToken cancellationToken)
    {
        // Movements carry a timestamp; asOf is a business date. Everything that
        // happened before midnight at the end of that day counts.
        DateTimeOffset cutoff = new DateTimeOffset(
            asOf.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var movements = await _db.StockMovements
            .AsNoTracking()
            .Where(m => m.OrganizationId == organizationId && m.OccurredAt < cutoff)
            .ToListAsync(cancellationToken);

        var products = await _db.Products
            .AsNoTracking()
            .Where(p => p.OrganizationId == organizationId)
            .OrderBy(p => p.Sku)
            .ToListAsync(cancellationToken);

        var rows = new List<StockValuationRow>();
        long totalValueMinor = 0;

        foreach (var product in products)
        {
            var productMovements = new List<StockMovement>();

            foreach (var movement in movements)
            {
                if (movement.ProductId == product.Id)
                {
                    productMovements.Add(movement);
                }
            }

            // A product that has never moved holds nothing worth valuing.
            if (productMovements.Count == 0)
            {
                continue;
            }

            // The costing rules live in StockService and stay there. This
            // report only asks the questions and adds up the answers.
            decimal quantity = _stockService.CurrentQuantity(productMovements);
            long unitCostMinor = _stockService.CalculateWeightedAverageCost(productMovements);

            long valueMinor = (long)Math.Round(
                quantity * unitCostMinor, MidpointRounding.AwayFromZero);

            rows.Add(new StockValuationRow
            {
                ProductId = product.Id,
                Sku = product.Sku,
                Name = product.Name,
                Quantity = quantity,
                UnitCostMinor = unitCostMinor,
                ValueMinor = valueMinor
            });

            totalValueMinor = totalValueMinor + valueMinor;
        }

        return new StockValuationReport
        {
            AsOf = asOf,
            Rows = rows,
            TotalValueMinor = totalValueMinor
        };
    }

    public async Task<DaySheetReport> GetDaySheetAsync(
        Guid organizationId,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        var sales = await _db.Sales
            .AsNoTracking()
            .Where(s => s.OrganizationId == organizationId && s.SaleDate == date)
            .ToListAsync(cancellationToken);

        long grossSalesMinor = 0;

        foreach (var sale in sales)
        {
            grossSalesMinor = grossSalesMinor + sale.TotalMinor;
        }

        var paymentTotals = await (
            from payment in _db.Payments.AsNoTracking()
            join sale in _db.Sales.AsNoTracking()
                on payment.SaleId equals sale.Id
            where payment.OrganizationId == organizationId
                && sale.OrganizationId == organizationId
                && sale.SaleDate == date
            group payment by payment.Method
            into grouped
            orderby grouped.Key
            select new PaymentMethodTotal
            {
                Method = grouped.Key,
                AmountMinor = grouped.Sum(p => p.AmountMinor)
            })
            .ToListAsync(cancellationToken);

        long expectedCashMinor = 0;

        foreach (var total in paymentTotals)
        {
            if (total.Method == PaymentMethod.Cash)
            {
                expectedCashMinor = total.AmountMinor;
            }
        }

        var topProducts = await (
            from saleLine in _db.SaleLines.AsNoTracking()
            join sale in _db.Sales.AsNoTracking()
                on saleLine.SaleId equals sale.Id
            join product in _db.Products.AsNoTracking()
                on saleLine.ProductId equals product.Id
            where saleLine.OrganizationId == organizationId
                && sale.OrganizationId == organizationId
                && product.OrganizationId == organizationId
                && sale.SaleDate == date
            group saleLine by new
            {
                product.Id,
                product.Sku,
                product.Name
            }
            into grouped
            orderby grouped.Sum(l => l.Quantity) descending
            select new TopProductRow
            {
                ProductId = grouped.Key.Id,
                Sku = grouped.Key.Sku,
                Name = grouped.Key.Name,
                QuantitySold = grouped.Sum(l => l.Quantity)
            })
            .Take(5)
            .ToListAsync(cancellationToken);

        return new DaySheetReport
        {
            Date = date,
            SaleCount = sales.Count,
            GrossSalesMinor = grossSalesMinor,
            PaymentTotals = paymentTotals,
            ExpectedCashMinor = expectedCashMinor,
            TopProducts = topProducts
        };
    }
}
