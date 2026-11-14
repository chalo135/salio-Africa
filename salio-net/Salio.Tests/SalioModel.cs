using Microsoft.EntityFrameworkCore;
using Salio.Infrastructure;

namespace Salio.Tests
{
    // A SalioDbContext that is only ever asked for its model: the list of
    // tables and columns EF Core builds from the configuration classes.
    // Building the model does not open a connection, so no Postgres is
    // needed. The host name below does not exist on purpose: if anything
    // ever did try to connect, the test would fail instead of quietly
    // reaching a real database.
    internal static class SalioModel
    {
        public static SalioDbContext NewContext()
        {
            DbContextOptions<SalioDbContext> options = new DbContextOptionsBuilder<SalioDbContext>()
                .UseNpgsql("Host=no-database.invalid;Database=none;Username=none;Password=none")
                .Options;

            return new SalioDbContext(options);
        }
    }
}
