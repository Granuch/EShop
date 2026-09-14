using EShop.BuildingBlocks.Infrastructure.Http;
using Microsoft.Extensions.Hosting;
using Moq;

namespace EShop.BuildingBlocks.UnitTests.Http;

/// <summary>Ordering audit L10 / D15: API docs everywhere except Production, in every service.</summary>
[TestFixture]
public class EShopApiDocsTests
{
    private static IHostEnvironment Environment(string name)
    {
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(x => x.EnvironmentName).Returns(name);
        return environment.Object;
    }

    [TestCase("Development")]
    [TestCase("Testing")]
    [TestCase("Sandbox")]
    [TestCase("Staging")]
    public void TheDocs_AreServedOutsideProduction(string environment)
        => Assert.That(EShopApiDocs.IsExposedIn(Environment(environment)), Is.True);

    [Test]
    public void TheDocs_AreNotServedInProduction()
        => Assert.That(EShopApiDocs.IsExposedIn(Environment("Production")), Is.False);
}
