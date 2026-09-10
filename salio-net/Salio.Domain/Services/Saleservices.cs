namespace Salio.Domain.Services;

using Salio.Domain.Entities;

public class SaleService
{
    public List<JournalLine> BuildJournalLines(
        Sale sale,
        List<SaleLine> lines,
        Guid cashAccountId,
        Guid salesAccountId,
        Guid cogsAccountId,
        Guid inventoryAccountId)
    {
        if (lines.Count == 0)
        {
            throw new InvalidOperationException("A sale must have at least one line");
        }

        long totalSale = 0;
        long totalCost = 0;

        foreach (var line in lines)
        {
            totalSale = totalSale + line.LineTotalMinor;
            totalCost = totalCost + line.UnitCostMinor;
        }

        var result = new List<JournalLine>();

        result.Add(NewLine(sale, cashAccountId, totalSale, 0));
        result.Add(NewLine(sale, salesAccountId, 0, totalSale));
        result.Add(NewLine(sale, cogsAccountId, totalCost, 0));
        result.Add(NewLine(sale, inventoryAccountId, 0, totalCost));

        return result;
    }

    private JournalLine NewLine(Sale sale, Guid accountId, long debit, long credit)
    {
        return new JournalLine
        {
            Id = Guid.NewGuid(),
            OrganizationId = sale.OrganizationId,
            AccountId = accountId,
            DebitMinor = debit,
            CreditMinor = credit,
            Currency = "KES"
        };
    }
}