using System.Net.Http.Headers;
using System.Net.Http.Json;
using EShop.Identity.IntegrationTests.Models;

namespace EShop.Identity.IntegrationTests.Helpers;

/// <summary>
/// Shared helper methods for authentication in tests
/// </summary>
public static class AuthHelper
{
    public static async Task<LoginResponse> LoginAsync(
        this HttpClient client, 
        string email, 
        string password,
        string? twoFactorCode = null)
    {
        var request = new LoginRequest 
        { 
            Email = email, 
            Password = password,
            TwoFactorCode = twoFactorCode
        };
        
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", request);
        response.EnsureSuccessStatusCode();
        
        var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
        return result!;
    }

    public static void SetBearerToken(this HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public static void ClearBearerToken(this HttpClient client)
    {
        client.DefaultRequestHeaders.Authorization = null;
    }

    public static async Task<string> GetAccessTokenAsync(
        this HttpClient client,
        string email,
        string password)
    {
        var loginResponse = await client.LoginAsync(email, password);
        return loginResponse.AccessToken;
    }

    public static async Task<(string AccessToken, string RefreshToken)> GetTokensAsync(
        this HttpClient client,
        string email,
        string password)
    {
        var loginResponse = await client.LoginAsync(email, password);
        return (loginResponse.AccessToken, loginResponse.RefreshToken);
    }
}
