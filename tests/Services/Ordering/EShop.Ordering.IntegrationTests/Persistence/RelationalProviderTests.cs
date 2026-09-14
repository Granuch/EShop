using EShop.Ordering.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Ordering.IntegrationTests.Persistence;

/// <summary>
/// Ordering audit M11. The suite runs on real PostgreSQL, and this fails if it quietly stops doing so — a
/// factory defaulting back to InMemory, or <c>Testing:UseRelationalDatabase</c> no longer reaching
/// Program.cs, would otherwise leave every other test green while testing a different provider.
/// </summary>
[TestFixture]
[Category("Integration")]
public class RelationalProviderTests : IntegrationTestBase
{
    [Test]
    public void TheHost_UsesPostgreSql()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();

        db.Database.ProviderName.Should().Be("Npgsql.EntityFrameworkCore.PostgreSQL");
    }

    /// <summary>
    /// The schema comes from the migration chain, not <c>EnsureCreated</c>: the history table is populated
    /// and nothing is pending, so the host's startup migration found the database current.
    /// </summary>
    [Test]
    public async Task TheSchema_ComesFromTheMigrations_WithNothingPending()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();

        (await db.Database.GetAppliedMigrationsAsync()).Should().NotBeEmpty();
        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
    }
}
