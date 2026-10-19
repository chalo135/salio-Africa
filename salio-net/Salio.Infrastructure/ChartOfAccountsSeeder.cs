using Microsoft.EntityFrameworkCore;
using Salio.Domain.Entities;
using Salio.Domain.Enums;

namespace Salio.Infrastructure
{
    public static class ChartOfAccountsSeeder
    {
        public static async Task SeedAsync(
            SalioDbContext db,
            Guid organizationId,
            CancellationToken cancellationToken)
        {
            List<Account> standard = new()
            {
                NewAccount(organizationId, "1000", "Cash", AccountClass.Asset),
                NewAccount(organizationId, "1010", "M-Pesa", AccountClass.Asset),

                // What customers owe for goods taken on credit. Each line on it
                // names its customer (JournalLine.CustomerId), so one customer's
                // balance is the sum of their lines here.
                NewAccount(organizationId, "1100", "Accounts Receivable", AccountClass.Asset),

                NewAccount(organizationId, "1200", "Inventory", AccountClass.Asset),
                NewAccount(organizationId, "2000", "Accounts Payable", AccountClass.Liability),
                NewAccount(organizationId, "2100", "VAT Payable", AccountClass.Liability),
                NewAccount(organizationId, "3000", "Owner's Equity", AccountClass.Equity),

                // Money the owner takes out for the family. Equity, not an
                // expense: it is not a cost of running the shop.
                NewAccount(organizationId, "3100", "Drawings", AccountClass.Equity),

                NewAccount(organizationId, "4000", "Sales Revenue", AccountClass.Income),
                NewAccount(organizationId, "4200", "Sales Discounts", AccountClass.Income),
                NewAccount(organizationId, "5000", "Cost of Goods Sold", AccountClass.Expense),

                // Running costs. 5000 above is cost of sales; these are not.
                NewAccount(organizationId, "5100", "Rent - Shop", AccountClass.Expense),
                NewAccount(organizationId, "5200", "Electricity", AccountClass.Expense),
                NewAccount(organizationId, "5300", "Water", AccountClass.Expense),
                NewAccount(organizationId, "5400", "Wages - Casual", AccountClass.Expense),
                NewAccount(organizationId, "5500", "Transport", AccountClass.Expense),
                NewAccount(organizationId, "5600", "Bank and M-Pesa Charges", AccountClass.Expense),
                NewAccount(organizationId, "5900", "Other", AccountClass.Expense)
            };

            // Per code, not per shop. A shop seeded before an account was
            // added to the list above still gets the new one, and a shop
            // that has everything gets nothing. Switched-off accounts count
            // as present: they keep their code, so they are not made again.
            List<string> existingCodes = await db.Accounts
                .Where(a => a.OrganizationId == organizationId)
                .Select(a => a.Code)
                .ToListAsync(cancellationToken);

            List<Account> missing = new();

            foreach (var account in standard)
            {
                if (!existingCodes.Contains(account.Code))
                {
                    missing.Add(account);
                }
            }

            if (missing.Count == 0)
            {
                return;
            }

            db.Accounts.AddRange(missing);
            await db.SaveChangesAsync(cancellationToken);
        }

        private static Account NewAccount(
            Guid organizationId,
            string code,
            string name,
            AccountClass accountClass) => new()
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                Code = code,
                Name = name,
                AccountClass = accountClass,
                ParentId = null,
                IsActive = true
            };
    }
}
