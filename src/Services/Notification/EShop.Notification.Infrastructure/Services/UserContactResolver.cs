using System.Net.Http.Json;
using EShop.Notification.Application.Abstractions;
using EShop.Notification.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace EShop.Notification.Infrastructure.Services;

public sealed class UserContactResolver : IUserContactResolver
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<UserContactResolver> _logger;

    public UserContactResolver(HttpClient httpClient, ILogger<UserContactResolver> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<RecipientAddress?> ResolveAsync(string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        var attempt = 0;
        while (attempt < 3)
        {
            attempt++;

            try
            {
                using var response = await _httpClient.GetAsync($"api/v1/users/{userId}/contact", ct);
                if (!response.IsSuccessStatusCode)
                {
                    if ((int)response.StatusCode >= 500 && attempt < 3)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)), ct);
                        continue;
                    }

                    _logger.LogWarning(
                        "User contact endpoint returned status {StatusCode} for UserId={UserId}",
                        response.StatusCode,
                        userId);
                    return null;
                }

                var payload = await response.Content.ReadFromJsonAsync<UserContactResponse>(cancellationToken: ct);
                if (payload is null || string.IsNullOrWhiteSpace(payload.Email))
                {
                    return null;
                }

                var displayName = string.Join(' ', new[] { payload.FirstName, payload.LastName }
                    .Where(static value => !string.IsNullOrWhiteSpace(value)));

                return new RecipientAddress(payload.Email, string.IsNullOrWhiteSpace(displayName) ? null : displayName);
            }
            catch (HttpRequestException ex) when (attempt < 3)
            {
                _logger.LogWarning(ex, "Retrying contact resolution for UserId={UserId}. Attempt={Attempt}", userId, attempt);
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)), ct);
            }
        }

        _logger.LogWarning("Failed to resolve contact details for UserId={UserId} after retries", userId);
        return null;
    }

    private sealed record UserContactResponse
    {
        public string Email { get; init; } = string.Empty;
        public string? FirstName { get; init; }
        public string? LastName { get; init; }
    }
}
