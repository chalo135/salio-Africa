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
            bool alreadySeeded = await db.Accounts
                .AnyAsync(a => a.OrganizationId == organizationId, cancellationToken);

            if (alreadySeeded)
            {
                return;
            }

            List<Account> accounts = new()
            {
                NewAccount(organizationId, "1000", "Cash", AccountClass.Asset),
                NewAccount(organizationId, "1010", "M-Pesa", AccountClass.Asset),
                NewAccount(organizationId, "1200", "Inventory", AccountClass.Asset),
                NewAccount(organizationId, "2000", "Accounts Payable", AccountClass.Liability),
                NewAccount(organizationId, "2100", "VAT Payable", AccountClass.Liability),
                NewAccount(organizationId, "3000", "Owner's Equity", AccountClass.Equity),
                NewAccount(organizationId, "4000", "Sales Revenue", AccountClass.Income),
                NewAccount(organizationId, "4200", "Sales Discounts", AccountClass.Income),
                NewAccount(organizationId, "5000", "Cost of Goods Sold", AccountClass.Expense)
            };

            db.Accounts.AddRange(accounts);
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
