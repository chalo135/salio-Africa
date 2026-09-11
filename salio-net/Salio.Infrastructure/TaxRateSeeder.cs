using Microsoft.EntityFrameworkCore;
using Salio.Domain.Entities;
using Salio.Domain.Enums;

namespace Salio.Infrastructure
{
    public static class TaxRateSeeder
    {
        public static async Task SeedAsync(
            SalioDbContext db,
            Guid organizationId,
            CancellationToken cancellationToken)
        {
            bool alreadySeeded = await db.TaxRates
                .AnyAsync(r => r.OrganizationId == organizationId
                    && r.TaxType == TaxType.TurnoverTax, cancellationToken);

            if (alreadySeeded)
            {
                return;
            }

            // CONTESTED RATE - VERIFY WITH KRA BEFORE ANY REAL CUSTOMER USES IT.
            // Sources disagree on Kenyan Turnover Tax, and it has changed three
            // times since 2020 (1%, then 3%, then 1.5%). This is demo data only.
            db.TaxRates.Add(new TaxRate
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                TaxType = TaxType.TurnoverTax,
                Band = null,
                RatePercent = 1.5m,
                EffectiveFrom = new DateOnly(2024, 12, 27),
                EffectiveTo = null,
                Source = "KRA Turnover Tax page, kra.go.ke; Tax Procedures (Amendment) Act 2024 cut 3% to 1.5% from 27 Dec 2024"
            });

            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
