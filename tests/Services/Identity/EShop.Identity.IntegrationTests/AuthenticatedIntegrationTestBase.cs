using EShop.Identity.IntegrationTests.Helpers;

namespace EShop.Identity.IntegrationTests;

/// <summary>
/// Base class for integration tests that require authentication
/// Automatically logs in and sets the bearer token before each test
/// </summary>
[Category("Integration")]
public abstract class AuthenticatedIntegrationTestBase : IntegrationTestBase
{
    protected string AccessToken { get; private set; } = string.Empty;
    protected string RefreshToken { get; private set; } = string.Empty;

    protected virtual string TestUserEmail => TestUsers.Admin.Email;
    protected virtual string TestUserPassword => TestUsers.Admin.Password;

    [SetUp]
    public override async Task SetUpAsync()
    {
        await base.SetUpAsync();
        
        var (accessToken, refreshToken) = await Client.GetTokensAsync(TestUserEmail, TestUserPassword);
        AccessToken = accessToken;
        RefreshToken = refreshToken;
        Client.SetBearerToken(accessToken);
    }

    [TearDown]
    public override async Task TearDownAsync()
    {
        Client.ClearBearerToken();
        await base.TearDownAsync();
    }
}
