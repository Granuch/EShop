using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EShop.Catalog.Infrastructure.Data;

public class CatalogDbContextFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<CatalogDbContext>();

        // Use your PostgreSQL connection string
        optionsBuilder.UseNpgsql("Host=localhost;Port=5432;Database=EShopCatalog;Username=postgres;Password=2607");

        return new CatalogDbContext(optionsBuilder.Options);
    }
}