using EShop.Identity.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Identity.IntegrationTests;

/// <summary>
/// Base class for integration tests that require transaction rollback
/// Each test runs in a transaction that is rolled back after the test completes
/// </summary>
[Category("Integration")]
public abstract class TransactionalIntegrationTestBase : IntegrationTestBase
{
    private IServiceScope? _transactionScope;
    private IdentityDbContext? _dbContext;

    [SetUp]
    public override async Task SetUpAsync()
    {
        await base.SetUpAsync();
        
        _transactionScope = Factory.Services.CreateScope();
        _dbContext = _transactionScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        
        await _dbContext.Database.BeginTransactionAsync();
    }

    [TearDown]
    public override async Task TearDownAsync()
    {
        if (_dbContext?.Database.CurrentTransaction != null)
        {
            await _dbContext.Database.RollbackTransactionAsync();
        }
        
        _transactionScope?.Dispose();
        _transactionScope = null;
        _dbContext = null;
        
        await base.TearDownAsync();
    }
}
