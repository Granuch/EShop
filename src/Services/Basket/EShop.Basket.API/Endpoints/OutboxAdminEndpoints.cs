using EShop.Basket.Infrastructure.Outbox;

namespace EShop.Basket.API.Endpoints;

/// <summary>
/// Basket audit S7 (H6, D7). A dead-lettered checkout is an order Ordering never received, and until this stage nothing
/// could bring one back. Admin only; reached through the gateway's existing <c>/api/v1/basket/{**catch-all}</c> route.
/// </summary>
public static class OutboxAdminEndpoints
{
    /// <summary>One request replays at most this many, oldest first; call it again for more.</summary>
    public const int MaxReplayPerRequest = 1000;

    public static void MapBasketOutboxAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/basket/admin/outbox")
            .WithTags("Basket outbox (admin)")
            .RequireAuthorization("Admin");

        group.MapGet("/dead-letters", async (BasketOutboxDeadLetters deadLetters) =>
            Results.Ok(new { count = await deadLetters.CountAsync() }))
        .WithName("CountOutboxDeadLetters");

        group.MapPost("/dead-letters/replay", async (BasketOutboxDeadLetters deadLetters, ILoggerFactory loggerFactory) =>
        {
            var replayed = await deadLetters.ReplayAsync(MaxReplayPerRequest);

            loggerFactory.CreateLogger(nameof(OutboxAdminEndpoints))
                .LogWarning("Replayed {Replayed} dead-lettered basket outbox messages", replayed);

            return Results.Ok(new { replayed });
        })
        .WithName("ReplayOutboxDeadLetters");
    }
}
