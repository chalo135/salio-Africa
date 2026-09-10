using Microsoft.EntityFrameworkCore;
using Salio.Domain.Entities;

namespace Salio.Infrastructure;

public class SalioDbContext : DbContext
{
    public SalioDbContext(DbContextOptions<SalioDbContext> options)
        : base(options)
    {
    }

    public DbSet<Organization> Organizations => Set<Organization>();
}