using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EShop.Notification.Application.Abstractions;
using EShop.Notification.Domain.ValueObjects;
using EShop.Notification.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EShop.Notification.Infrastructure.Services;

/// <summary>
/// Looks the recipient up at Identity's <c>GET api/v1/users/{id}/contact</c> (InternalService API key).
/// <para>Notification audit S3 (D3, M1, L20). Every answer used to collapse into <c>null</c>, so a deleted user, a wrong
/// API key and an outage all looked alike and were all retried. Now:</para>
/// <list type="bullet">
///   <item>200 with an address → found; 200 without a usable address, 404 or 400 → undeliverable, never retried;</item>
///   <item>401 or 403 → <see cref="UserContactUnavailableException"/> marked as a configuration error, not retried here;</item>
///   <item>anything else — 5xx, 408, 429, a timeout, an unreachable host, an unreadable body — is retried here
///   (<see cref="MaxAttempts"/> attempts, doubling delays), then reported as <see cref="UserContactUnavailableException"/>.
///   The timeout and the body used to escape the retry.</item>
/// </list>
/// </summary>
public sealed class UserContactResolver : IUserContactResolver
{
    public const int MaxAttempts = 3;

    private readonly HttpClient _httpClient;
    private readonly ILogger<UserContactResolver> _logger;
    private readonly TimeSpan _retryBaseDelay;

    public UserContactResolver(
        HttpClient httpClient,
        IOptions<IdentityServiceSettings> settings,
        ILogger<UserContactResolver> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _retryBaseDelay = TimeSpan.FromMilliseconds(Math.Max(0, settings.Value.RetryBaseDelayMilliseconds));
    }

    public async Task<RecipientLookup> ResolveAsync(string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return RecipientLookup.Undeliverable("The event carries no UserId.");
        }

        for (var attempt = 1; ; attempt++)
        {
            string transientReason;
            Exception? transientException = null;

            try
            {
                using var response = await _httpClient.GetAsync($"api/v1/users/{Uri.EscapeDataString(userId)}/contact", ct);
                switch (response.StatusCode)
                {
                    case HttpStatusCode.OK:
                        return await ReadContactAsync(response, ct);
                    case HttpStatusCode.NotFound:
                        return RecipientLookup.Undeliverable("Identity has no such user (404).");
                    case HttpStatusCode.BadRequest:
                        return RecipientLookup.Undeliverable("Identity rejected the user id (400).");
                    case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                        throw new UserContactUnavailableException(
                            $"Identity refused Notification's API key ({(int)response.StatusCode}). Check IdentityService:ApiKey.",
                            isConfigurationError: true);
                }

                transientReason = $"Identity answered {(int)response.StatusCode}.";
            }
            catch (HttpRequestException ex)
            {
                transientReason = "Identity could not be reached.";
                transientException = ex;
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                // HttpClient.Timeout, not the caller: the caller's cancellation propagates.
                transientReason = "Identity did not answer in time.";
                transientException = ex;
            }
            catch (JsonException ex)
            {
                transientReason = "Identity's contact response could not be read.";
                transientException = ex;
            }

            if (attempt >= MaxAttempts)
            {
                throw new UserContactUnavailableException(
                    $"{transientReason} Gave up after {MaxAttempts} attempts.",
                    isConfigurationError: false,
                    transientException);
            }

            _logger.LogWarning(
                transientException,
                "Contact lookup for UserId={UserId} failed: {Reason} Attempt {Attempt} of {MaxAttempts}.",
                userId, transientReason, attempt, MaxAttempts);
            await Task.Delay(_retryBaseDelay * Math.Pow(2, attempt - 1), ct);
        }
    }

    private static async Task<RecipientLookup> ReadContactAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var payload = await response.Content.ReadFromJsonAsync<UserContactResponse>(cancellationToken: ct);
        if (payload is null || string.IsNullOrWhiteSpace(payload.Email))
        {
            return RecipientLookup.Undeliverable("Identity has no email address for the user.");
        }

        var displayName = string.Join(' ', new[] { payload.FirstName, payload.LastName }
            .Where(static value => !string.IsNullOrWhiteSpace(value)));

        try
        {
            return RecipientLookup.Found(
                new RecipientAddress(payload.Email, string.IsNullOrWhiteSpace(displayName) ? null : displayName));
        }
        catch (ArgumentException)
        {
            return RecipientLookup.Undeliverable("Identity's email address for the user is not valid.");
        }
    }

    private sealed record UserContactResponse
    {
        public string Email { get; init; } = string.Empty;
        public string? FirstName { get; init; }
        public string? LastName { get; init; }
    }
}
