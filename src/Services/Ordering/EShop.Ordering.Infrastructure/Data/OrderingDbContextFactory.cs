using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace EShop.Ordering.Infrastructure.Data;

/// <summary>
/// Design-time DbContext factory for EF Core migrations.
/// </summary>
public class OrderingDbContextFactory : IDesignTimeDbContextFactory<OrderingDbContext>
{
    public OrderingDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Path.Combine(Directory.GetCurrentDirectory(), "../EShop.Ordering.API"))
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("OrderingDb")
            ?? "Host=localhost;Port=5434;Database=eshop_ordering;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<OrderingDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new OrderingDbContext(optionsBuilder.Options);
    }
}
