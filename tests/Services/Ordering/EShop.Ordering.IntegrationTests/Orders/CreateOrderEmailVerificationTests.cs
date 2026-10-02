using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using EShop.BuildingBlocks.Infrastructure.Authorization;
using EShop.Ordering.Infrastructure.Data;
using EShop.Ordering.IntegrationTests.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Ordering.IntegrationTests.Orders;

/// <summary>
/// Soft email verification: a customer signs in before confirming their address, but <c>POST /api/v1/orders</c> needs
/// the token's <c>email_verified=true</c> — else 403 <c>Auth.EmailNotConfirmed</c> in the shared envelope, and no
/// order. Checked in Ordering itself, through the real JwtBearer pipeline, so these also prove the claim arrives under
/// its short name. An admin creating an order on a user's behalf is exempt.
/// </summary>
[TestFixture]
[Category("Integration")]
public class CreateOrderEmailVerificationTests : AuthenticatedIntegrationTestBase
{
    private const string OrdersEndpoint = "/api/v1/orders";

    protected override string TestUserRole => "Customer";
    protected override string TestUserId => "unverified-customer-1";

    private CreateOrderRequest Request(string? userId = null) => new()
    {
        UserId = userId ?? TestUserId,
        Street = "1 Verify Lane",
        City = "TestTown",
        State = "CA",
        ZipCode = "90210",
        Country = "US",
        Items = [new CreateOrderItemRequest { ProductId = Factory.Catalog.Add("Widget", 10.00m), Quantity = 1 }]
    };

    /// <summary>Sends the request with <paramref name="token"/>, which replaces the fixture's default bearer header.</summary>
    private Task<HttpResponseMessage> CreateOrderAsync(string token, CreateOrderRequest request)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, OrdersEndpoint) { Content = JsonContent.Create(request) };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return Client.SendAsync(message);
    }

    private async Task<int> StoredOrderCountAsync(string userId)
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<OrderingDbContext>()
            .Orders.CountAsync(o => o.UserId == userId);
    }

    private static Claim EmailVerified(string value) => new(EmailVerification.ClaimType, value, ClaimValueTypes.Boolean);

    [Test]
    public async Task AnUnverifiedCustomer_Gets403AuthEmailNotConfirmed_AndNoOrder()
    {
        var before = await StoredOrderCountAsync(TestUserId);

        var response = await CreateOrderAsync(CreateToken("Customer", EmailVerified("false")), Request());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var problem = (await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>())!;
        problem.ErrorCode.Should().Be("Auth.EmailNotConfirmed");
        problem.Detail.Should().Be(EmailVerification.NotConfirmed.Message);
        problem.TraceId.Should().NotBeNullOrEmpty();
        (await StoredOrderCountAsync(TestUserId)).Should().Be(before);
    }

    [Test]
    public async Task ATokenWithoutTheClaim_IsRefusedTheSameWay()
    {
        // Every token issued before Identity added the claim. Fail closed: one 403, then the client refreshes.
        var response = await CreateOrderAsync(CreateTokenWithoutEmailVerified("Customer"), Request());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>())!.ErrorCode
            .Should().Be("Auth.EmailNotConfirmed");
    }

    [Test]
    public async Task AVerifiedCustomer_PlacesTheOrder()
    {
        var before = await StoredOrderCountAsync(TestUserId);

        var response = await CreateOrderAsync(CreateToken("Customer", EmailVerified("true")), Request());

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        (await StoredOrderCountAsync(TestUserId)).Should().Be(before + 1);
    }

    [Test]
    public async Task AnAdmin_CreatingAnOrderForAUser_IsExempt()
    {
        // The admin's own address is not what is being vouched for, and Ordering cannot see the target user's flag.
        const string customer = "customer-of-an-admin-order";
        var before = await StoredOrderCountAsync(customer);

        var response = await CreateOrderAsync(CreateTokenWithoutEmailVerified("Admin"), Request(customer));

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        (await StoredOrderCountAsync(customer)).Should().Be(before + 1);
    }
}
