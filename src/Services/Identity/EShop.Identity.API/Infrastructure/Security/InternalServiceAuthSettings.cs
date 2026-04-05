namespace EShop.Identity.API.Infrastructure.Security;

public sealed class InternalServiceAuthSettings
{
    public const string SectionName = "InternalServiceAuth";

    public string ApiKey { get; init; } = string.Empty;

    public string HeaderName { get; init; } = "X-Internal-Api-Key";
}
