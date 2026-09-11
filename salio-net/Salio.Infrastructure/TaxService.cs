using Microsoft.EntityFrameworkCore;
using Salio.Domain;
using Salio.Domain.Entities;
using Salio.Domain.Enums;
using Salio.Infrastructure.Tax;

namespace Salio.Infrastructure;

/// <summary>
/// Works out what is owed. It never files anything and never talks to KRA,
/// iTax or eTIMS: the shopkeeper takes the figure and files it herself.
/// </summary>
public class TaxService
{
    private readonly SalioDbContext _db;

    public TaxService(SalioDbContext db)
    {
        _db = db;
    }

    public async Task<TurnoverTaxResult> GetTurnoverTaxAsync(
        Guid organizationId,
        DateOnly from,
        DateOnly to,
        CancellationToken ct)
    {
        if (to < from)
        {
            throw new InvalidOperationException("The period ends before it starts");
        }

        // 'from' is a LINQ query keyword, so copy both dates into ordinary
        // locals before using them in the query below.
        DateOnly fromDate = from;
        DateOnly toDate = to;

        var incomeLines =
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
                && account.AccountClass == AccountClass.Income
            select line;

        // Income sits on the credit side. Debits are discounts and refunds,
        // so they come off turnover rather than being ignored.
        long creditsMinor = await incomeLines.SumAsync(l => l.CreditMinor, ct);
        long debitsMinor = await incomeLines.SumAsync(l => l.DebitMinor, ct);
        long grossTurnoverMinor = creditsMinor - debitsMinor;

        // A rate covers the period only if it was in force on the first day
        // and still in force on the last (EffectiveTo is the last day it
        // applies). If the rate changed part-way through, nothing matches.
        var rates = await _db.TaxRates
            .AsNoTracking()
            .Where(r => r.OrganizationId == organizationId
                && r.TaxType == TaxType.TurnoverTax
                && r.EffectiveFrom <= fromDate
                && (r.EffectiveTo == null || r.EffectiveTo >= toDate))
            .ToListAsync(ct);

        string period = from.ToString("yyyy-MM-dd") + " to " + to.ToString("yyyy-MM-dd");

        if (rates.Count == 0)
        {
            throw new InvalidOperationException(
                "No Turnover Tax rate covers " + period
                + ". Add one to TaxRates, or split the period where the rate changed.");
        }

        if (rates.Count > 1)
        {
            throw new InvalidOperationException(
                "More than one Turnover Tax rate covers " + period
                + ". Fix the overlapping dates in TaxRates.");
        }

        var rate = rates[0];

        // Round once, at the end, like every other money figure in Salio.
        long taxDueMinor = (long)Math.Round(
            grossTurnoverMinor * rate.RatePercent / 100, MidpointRounding.AwayFromZero);

        return new TurnoverTaxResult
        {
            GrossTurnoverMinor = grossTurnoverMinor,
            RatePercent = rate.RatePercent,
            RateEffectiveFrom = rate.EffectiveFrom,
            RateSource = rate.Source,
            TaxDueMinor = taxDueMinor
        };
    }

    public async Task<FilingPack> GetFilingPackAsync(
        Guid organizationId,
        DateOnly from,
        DateOnly to,
        CancellationToken ct)
    {
        // Throws if there is no rate. A pack without a figure is no use.
        TurnoverTaxResult tax = await GetTurnoverTaxAsync(organizationId, from, to, ct);

        DateOnly fromDate = from;
        DateOnly toDate = to;

        // Drafts never reached the books, so they are not behind the figure.
        var sales = await _db.Sales
            .AsNoTracking()
            .Where(s => s.OrganizationId == organizationId
                && s.SaleDate >= fromDate
                && s.SaleDate <= toDate
                && s.Status != SaleStatus.Draft)
            .OrderBy(s => s.SaleNumber)
            .ToListAsync(ct);

        var payments = await (
            from payment in _db.Payments.AsNoTracking()
            join sale in _db.Sales.AsNoTracking()
                on payment.SaleId equals sale.Id
            where payment.OrganizationId == organizationId
                && sale.OrganizationId == organizationId
                && sale.SaleDate >= fromDate
                && sale.SaleDate <= toDate
            select payment)
            .ToListAsync(ct);

        var exceptions = new List<string>();
        long salesTotalMinor = 0;

        foreach (var sale in sales)
        {
            salesTotalMinor = salesTotalMinor + sale.TotalMinor;

            int paymentCount = 0;
            long paidMinor = 0;

            foreach (var payment in payments)
            {
                if (payment.SaleId == sale.Id)
                {
                    paymentCount = paymentCount + 1;
                    paidMinor = paidMinor + payment.AmountMinor;
                }
            }

            string saleName = "Sale " + sale.SaleNumber + " on " + sale.SaleDate.ToString("yyyy-MM-dd");

            if (paymentCount == 0)
            {
                exceptions.Add(saleName + " has no payment recorded. "
                    + "Record how the customer paid before you file.");
            }
            else if (paidMinor != sale.TotalMinor)
            {
                exceptions.Add(saleName + " is for " + Money.Kes(sale.TotalMinor)
                    + " but its payments add up to " + Money.Kes(paidMinor)
                    + ". Correct the sale or the payments before you file.");
            }
        }

        // The figure comes from the books, not the till. If the two disagree,
        // something reached an income account without being a sale.
        if (salesTotalMinor != tax.GrossTurnoverMinor)
        {
            exceptions.Add("Your sales add up to " + Money.Kes(salesTotalMinor)
                + " but the books show turnover of " + Money.Kes(tax.GrossTurnoverMinor)
                + ". Check any journal entries made by hand to income accounts before you file.");
        }

        return new FilingPack
        {
            From = from,
            To = to,
            TaxDueMinor = tax.TaxDueMinor,
            GrossTurnoverMinor = tax.GrossTurnoverMinor,
            RatePercent = tax.RatePercent,
            RateEffectiveFrom = tax.RateEffectiveFrom,
            SaleCount = sales.Count,
            Exceptions = exceptions
        };
    }

    // Records that the shopkeeper filed. This does not file anything: she has
    // already done that on iTax. Saving it closes the period's books.
    public async Task<FiledReturn> RecordFiledReturnAsync(
        Guid organizationId,
        DateOnly periodFrom,
        DateOnly periodTo,
        TaxType taxType,
        long amountMinor,
        DateTimeOffset filedAt,
        string acknowledgementReference,
        CancellationToken ct)
    {
        if (periodTo < periodFrom)
        {
            throw new InvalidOperationException("The period ends before it starts");
        }

        if (amountMinor < 0)
        {
            throw new InvalidOperationException("The amount filed cannot be negative");
        }

        if (string.IsNullOrWhiteSpace(acknowledgementReference))
        {
            throw new InvalidOperationException(
                "Enter the acknowledgement number iTax gave you when you filed");
        }

        // Two returns of the same tax for overlapping dates means one of them
        // is wrong, and there is no way to tell which.
        bool overlaps = await _db.FiledReturns
            .AnyAsync(r => r.OrganizationId == organizationId
                && r.TaxType == taxType
                && r.PeriodFrom <= periodTo
                && r.PeriodTo >= periodFrom, ct);

        if (overlaps)
        {
            throw new InvalidOperationException(
                "A " + taxType + " return has already been recorded for part of "
                + periodFrom.ToString("yyyy-MM-dd") + " to " + periodTo.ToString("yyyy-MM-dd"));
        }

        var filedReturn = new FiledReturn
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            PeriodFrom = periodFrom,
            PeriodTo = periodTo,
            TaxType = taxType,
            AmountMinor = amountMinor,
            FiledAt = filedAt,
            AcknowledgementReference = acknowledgementReference.Trim()
        };

        _db.FiledReturns.Add(filedReturn);
        await _db.SaveChangesAsync(ct);

        return filedReturn;
    }
}
