using EShop.Identity.Domain.Security;
using EShop.Identity.Infrastructure.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Identity.UnitTests.Infrastructure;

[TestFixture]
public class ServiceCollectionExtensionsTests
{
    [Test]
    public void AddIdentityInfrastructure_ShouldRegisterLoginAttemptTrackerOnce()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        services.AddIdentityInfrastructure(
            configuration,
            useInMemoryDatabase: true,
            inMemoryDatabaseName: $"IdentityUnitTestDb_{Guid.NewGuid()}",
            isDevelopment: true);

        var registrations = services
            .Where(s => s.ServiceType == typeof(ILoginAttemptTracker))
            .ToList();

        Assert.That(registrations, Has.Count.EqualTo(1));
    }
}
