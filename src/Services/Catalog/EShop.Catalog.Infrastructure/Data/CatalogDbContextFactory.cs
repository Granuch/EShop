using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace EShop.Catalog.Infrastructure.Data;

/// <summary>
/// Design-time DbContext factory for EF Core migrations.
/// Reads connection string from appsettings.json or environment variables.
/// </summary>
public class CatalogDbContextFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Path.Combine(Directory.GetCurrentDirectory(), "../EShop.Catalog.API"))
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("CatalogDb")
            ?? "Host=localhost;Port=5432;Database=EShopCatalog;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<CatalogDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new CatalogDbContext(optionsBuilder.Options);
    }
}