using System.Net;
using System.Net.Http.Json;
using EShop.BuildingBlocks.Domain.Outbox;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Catalog.Domain.Events;
using EShop.Catalog.Infrastructure.Data;
using EShop.Catalog.IntegrationTests.Fixtures;
using EShop.Catalog.IntegrationTests.Helpers;
using EShop.Catalog.IntegrationTests.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Catalog.IntegrationTests.Messaging;

/// <summary>
/// M7 (Catalog audit Stage 7): an integration event must carry the correlation id of the request
/// that caused it.
///
/// <para>
/// It did not. Catalog publishes through a two-hop chain: the request writes a <i>domain</i>-event
/// row (stamped with the request's id — that hop was always right), then
/// <c>OutboxProcessorService</c> dispatches it to a handler in a background scope, and the handler
/// enqueues the <i>integration</i>-event row. That scope has no <c>HttpContext</c>, so
/// <c>ICurrentUserContext.CorrelationId</c> minted a fresh GUID and the second row — the one that
/// actually leaves the service — carried an id belonging to nothing. The trace broke at the service
/// boundary, which is the one place a correlation id exists to cross.
/// </para>
///
/// <para>
/// This reads the real rows on real Postgres after the real processor has run, so it covers the
/// processor, the handler and the user context together. A handler unit test cannot: it mocks
/// <c>ICurrentUserContext</c>, which is exactly the component that was wrong.
/// </para>
/// </summary>
[TestFixture]
[Category("Integration")]
[Category("Postgres")]
public class OutboxCorrelationTests : AuthenticatedIntegrationTestBase
{
    private const string ProductsEndpoint = "/api/v1/products";
    private const string CorrelationHeader = "X-Correlation-ID";

    private static readonly string ProductCreatedIntegrationType = typeof(ProductCreatedIntegrationEvent).FullName!;
    private static readonly string PriceChangedIntegrationType = typeof(ProductPriceChangedIntegrationEvent).FullName!;
    private static readonly string ProductCreatedDomainType = typeof(ProductCreatedEvent).FullName!;

    protected override async Task<CatalogApiFactory> CreateFactoryAsync()
        => await FastOutboxApiFactory.CreateAsync();

    [Test]
    public async Task ProductCreatedIntegrationEvent_CarriesTheCreatingRequestsCorrelationId()
    {
        var correlationId = NewCorrelationId();

        var productId = await CreateProductAsync(correlationId);

        var domainRow = await WaitForRowAsync(ProductCreatedDomainType, productId);
        domainRow.CorrelationId.Should().Be(correlationId,
            "the first hop runs inside the request — if this fails, the header never reached the context at all");

        var integrationRow = await WaitForRowAsync(ProductCreatedIntegrationType, productId);
        integrationRow.CorrelationId.Should().Be(correlationId,
            "the handler runs in the outbox processor's scope and must report the dispatched row's id, not mint one");
        integrationRow.Payload.Should().Contain($"\"correlationId\":\"{correlationId}\"",
            "consumers read the id from the payload, so the event body must carry it too");
    }

    /// <summary>
    /// Basket re-prices live baskets from this event, so a price change is the trace most worth
    /// following across services.
    /// </summary>
    [Test]
    public async Task ProductPriceChangedIntegrationEvent_CarriesTheUpdatingRequestsCorrelationId()
    {
        Guid productId;
        using (var scope = Factory.Services.CreateScope())
        {
            var categoryId = await CatalogDataHelper.GetFirstCategoryIdAsync(scope.ServiceProvider);
            productId = await CatalogDataHelper.CreateProductAsync(
                scope.ServiceProvider, "Correlated Price", CatalogDataHelper.GenerateUniqueSku("COR"), 20m, 5, categoryId);
        }

        var correlationId = NewCorrelationId();
        var request = new HttpRequestMessage(HttpMethod.Put, $"{ProductsEndpoint}/{productId}")
        {
            Content = JsonContent.Create(new UpdateProductRequest { ProductId = productId, Price = 25m, StockQuantity = 5 })
        };
        request.Headers.Add(CorrelationHeader, correlationId);

        using (var response = await Client.SendAsync(request))
        {
            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var integrationRow = await WaitForRowAsync(PriceChangedIntegrationType, productId);
        integrationRow.CorrelationId.Should().Be(correlationId);
    }

    /// <summary>
    /// The processor dispatches a whole batch inside one DI scope, so the id has to be scoped per
    /// message. Two requests in quick succession normally land in the same batch.
    /// </summary>
    [Test]
    public async Task EventsDispatchedTogether_EachKeepTheirOwnCorrelationId()
    {
        var firstId = NewCorrelationId();
        var secondId = NewCorrelationId();

        var firstProduct = await CreateProductAsync(firstId);
        var secondProduct = await CreateProductAsync(secondId);

        (await WaitForRowAsync(ProductCreatedIntegrationType, firstProduct)).CorrelationId.Should().Be(firstId);
        (await WaitForRowAsync(ProductCreatedIntegrationType, secondProduct)).CorrelationId.Should().Be(secondId);
    }

    private static string NewCorrelationId() => Guid.NewGuid().ToString("N");

    private async Task<Guid> CreateProductAsync(string correlationId)
    {
        Guid categoryId;
        using (var scope = Factory.Services.CreateScope())
        {
            categoryId = await CatalogDataHelper.GetFirstCategoryIdAsync(scope.ServiceProvider);
        }

        var request = new HttpRequestMessage(HttpMethod.Post, ProductsEndpoint)
        {
            Content = JsonContent.Create(new CreateProductRequest
            {
                Name = "Correlated Product",
                Sku = CatalogDataHelper.GenerateUniqueSku("COR"),
                Price = 10m,
                StockQuantity = 3,
                CategoryId = categoryId
            })
        };
        request.Headers.Add(CorrelationHeader, correlationId);

        using var response = await Client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<CreatedResponse>())!.Id;
    }

    /// <summary>
    /// Polls for the outbox row of <paramref name="type"/> about <paramref name="productId"/>. The
    /// integration row only exists after the processor's second poll, so it cannot simply be read.
    /// </summary>
    private async Task<OutboxMessage> WaitForRowAsync(string type, Guid productId)
    {
        var needle = productId.ToString();
        var deadline = DateTime.UtcNow.AddSeconds(20);

        while (true)
        {
            using (var scope = Factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
                var rows = await db.OutboxMessages.AsNoTracking().Where(m => m.Type == type).ToListAsync();
                var row = rows.FirstOrDefault(m => m.Payload.Contains(needle, StringComparison.OrdinalIgnoreCase));
                if (row is not null)
                {
                    return row;
                }
            }

            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail($"No {type} outbox row for product {productId} appeared within 20 s.");
            }

            await Task.Delay(100);
        }
    }
}
