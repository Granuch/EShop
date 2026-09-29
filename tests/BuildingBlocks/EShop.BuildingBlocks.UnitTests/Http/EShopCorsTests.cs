using EShop.BuildingBlocks.Infrastructure.Http;
using Microsoft.AspNetCore.Cors.Infrastructure;

namespace EShop.BuildingBlocks.UnitTests.Http;

/// <summary>
/// Frontend-contracts F-04. A browser hides every response header outside the CORS-safelisted set unless the policy
/// exposes it, so these four are unreadable to a cross-origin client without it.
/// </summary>
[TestFixture]
public class EShopCorsTests
{
    [Test]
    public void ExposedHeaders_AreTheFourAClientMustRead()
    {
        Assert.That(EShopCors.ExposedHeaders,
            Is.EquivalentTo(new[] { "Location", "Retry-After", "X-Correlation-ID", "Content-Disposition" }));
    }

    [Test]
    public void WithEShopExposedHeaders_PutsThemOnThePolicy()
    {
        var policy = new CorsPolicyBuilder()
            .WithOrigins("http://localhost:3000")
            .WithEShopExposedHeaders()
            .Build();

        Assert.That(policy.ExposedHeaders, Is.EquivalentTo(EShopCors.ExposedHeaders));
    }
}
