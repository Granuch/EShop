using System.IdentityModel.Tokens.Jwt;
using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Infrastructure.Authorization;
using EShop.Identity.Domain.Entities;
using EShop.Identity.Domain.Interfaces;
using EShop.Identity.Infrastructure.Configuration;
using EShop.Identity.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Moq;

namespace EShop.Identity.UnitTests.Services;

/// <summary>
/// Soft email verification: every access token carries <c>email_verified</c>, read from
/// <see cref="ApplicationUser.EmailConfirmed"/> at the moment it is issued. Basket's checkout and Ordering's
/// <c>POST /orders</c> refuse anything but <c>true</c>, so a token without the claim would lock every customer out of
/// ordering, and one that always said true would let an unconfirmed customer order.
/// </summary>
[TestFixture]
public class TokenServiceClaimsTests
{
    [TestCase(true, "true")]
    [TestCase(false, "false")]
    public async Task TheAccessToken_CarriesEmailVerified_FromTheUser(bool emailConfirmed, string expected)
    {
        var user = new ApplicationUser
        {
            Id = "user-1",
            Email = "user@test.com",
            FirstName = "Test",
            LastName = "User",
            EmailConfirmed = emailConfirmed
        };

        var token = await CreateService().GenerateAccessTokenAsync(user, CancellationToken.None);

        var claims = new JwtSecurityTokenHandler().ReadJwtToken(token).Claims
            .Where(c => c.Type == EmailVerification.ClaimType)
            .ToList();
        Assert.That(claims, Has.Count.EqualTo(1));
        Assert.That(claims[0].Value, Is.EqualTo(expected));
    }

    [Test]
    public async Task TheClaim_IsAJsonBoolean_NotAString()
    {
        var user = new ApplicationUser { Id = "user-1", Email = "user@test.com", EmailConfirmed = true };

        var token = await CreateService().GenerateAccessTokenAsync(user, CancellationToken.None);

        // The OpenID Connect claim is a boolean; a client decoding the token should read true, not "true".
        var payload = new JwtSecurityTokenHandler().ReadJwtToken(token).Payload;
        Assert.That(payload[EmailVerification.ClaimType], Is.EqualTo(true));
    }

    private static TokenService CreateService()
    {
        var settings = Options.Create(new JwtSettings
        {
            SecretKey = "THIS_IS_A_TEST_ONLY_SECRET_KEY_32_CHARS_MINIMUM",
            Issuer = "issuer",
            Audience = "audience",
            AccessTokenExpirationMinutes = 60
        });

        var roles = new Mock<ICachedUserRolesService>();
        roles.Setup(x => x.GetRolesAsync(It.IsAny<ApplicationUser>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "Customer" });

        var store = new Mock<IUserStore<ApplicationUser>>();
        var userManager = new Mock<UserManager<ApplicationUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        return new TokenService(
            settings,
            userManager.Object,
            Mock.Of<IRefreshTokenRepository>(),
            Mock.Of<IUnitOfWork>(),
            roles.Object,
            Mock.Of<IRevokedTokenCache>());
    }
}
