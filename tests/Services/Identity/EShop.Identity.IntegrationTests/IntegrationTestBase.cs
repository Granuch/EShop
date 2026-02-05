using EShop.Identity.IntegrationTests.Fixtures;
using EShop.Identity.IntegrationTests.Helpers;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Identity.IntegrationTests;

/// <summary>
/// Base class for all Integration tests
/// Provides common functionality and HTTP client
/// </summary>
[Category("Integration")]
public abstract class IntegrationTestBase : IDisposable
{
    protected IdentityApiFactory Factory { get; private set; } = null!;
    protected HttpClient Client { get; set; } = null!;
    protected IServiceScope? ServiceScope { get; private set; }

    [SetUp]
    public virtual async Task SetUpAsync()
    {
        Factory = CreateFactory();
        Client = Factory.CreateClient();
        await Factory.InitializeDatabaseAsync();
    }

    [TearDown]
    public virtual async Task TearDownAsync()
    {
        ServiceScope?.Dispose();
        await Task.CompletedTask;
    }

    protected virtual IdentityApiFactory CreateFactory()
    {
        return new IdentityApiFactory();
    }

    protected IServiceScope CreateScope()
    {
        ServiceScope?.Dispose();
        ServiceScope = Factory.Services.CreateScope();
        return ServiceScope;
    }

    protected async Task<string> CreateTestUserAsync(
        string? email = null,
        string? password = null,
        string role = TestUsers.Roles.User)
    {
        using var scope = Factory.Services.CreateScope();
        return await UserManagementHelper.CreateTestUserAsync(
            scope.ServiceProvider, 
            email, 
            password, 
            role);
    }

    protected async Task DeleteTestUserAsync(string userId)
    {
        using var scope = Factory.Services.CreateScope();
        await UserManagementHelper.DeleteTestUserAsync(scope.ServiceProvider, userId);
    }

    public void Dispose()
    {
        ServiceScope?.Dispose();
        Client?.Dispose();
        Factory?.Dispose();
        GC.SuppressFinalize(this);
    }
}
