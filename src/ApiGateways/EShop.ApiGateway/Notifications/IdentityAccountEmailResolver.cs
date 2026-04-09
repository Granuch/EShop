using System.Net.Http.Json;

namespace EShop.ApiGateway.Notifications;

public sealed class IdentityAccountEmailResolver : IAccountEmailResolver
{
    private readonly HttpClient _httpClient;

    public IdentityAccountEmailResolver(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<string?> ResolveByUserIdAsync(string? userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        using var response = await _httpClient.GetAsync($"api/v1/users/{Uri.EscapeDataString(userId)}/contact", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var payload = await response.Content.ReadFromJsonAsync<UserContactResponse>(cancellationToken: cancellationToken);
        return payload?.Email;
    }

    private sealed record UserContactResponse
    {
        public string? Email { get; init; }
    }
}
