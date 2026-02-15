using EShop.Catalog.Application.Categories.Commands.CreateCategory;
using EShop.Catalog.Application.Categories.Commands.DeleteCategory;
using EShop.Catalog.Application.Categories.Commands.UpdateCategory;
using EShop.Catalog.Application.Categories.Queries.GetCategories;
using EShop.Catalog.Application.Categories.Queries.GetCategoryById;
using EShop.Catalog.Application.Products.Queries.GetProductByCategory;
using MediatR;

namespace EShop.Catalog.API.Endpoints;

/// <summary>
/// Category endpoints using Minimal API.
/// Caching is handled by CachingBehavior in the MediatR pipeline via ICacheableQuery.
/// Cache invalidation is handled by CacheInvalidationBehavior via ICacheInvalidatingCommand.
/// </summary>
public static class CategoryEndpoints
{
    public static void MapCategoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/categories")
            .WithTags("Categories");

        // GET /api/v1/categories (hierarchical structure)
        group.MapGet("/", async (IMediator mediator) =>
        {
            var result = await mediator.Send(new GetCategoriesQuery());

            return result.Match(
                value => Results.Ok(value),
                error => Results.BadRequest(new { error = error.Code, message = error.Message }));
        })
        .WithName("GetCategories")
        .Produces<object>(StatusCodes.Status200OK);

        // GET /api/v1/categories/{id}
        group.MapGet("/{id:guid}", async (Guid id, IMediator mediator) =>
        {
            var result = await mediator.Send(new GetCategoryByIdQuery { Id = id });

            return result.Match(
                value => Results.Ok(value),
                error => Results.NotFound(new { error = error.Code, message = error.Message }));
        })
        .WithName("GetCategoryById")
        .Produces<object>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        // GET /api/v1/categories/{id}/products
        group.MapGet("/{id:guid}/products", async (Guid id, IMediator mediator) =>
        {
            var result = await mediator.Send(new GetProductByCategoryQuery { CategoryId = id });

            return result.Match(
                value => Results.Ok(value),
                error => Results.NotFound(new { error = error.Code, message = error.Message }));
        })
        .WithName("GetProductsByCategory")
        .Produces<object>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        // POST /api/v1/categories (admin only)
        group.MapPost("/", async (CreateCategoryCommand command, IMediator mediator) =>
        {
            var result = await mediator.Send(command);

            return result.Match(
                value => Results.Created($"/api/v1/categories/{value}", new { id = value }),
                error => Results.BadRequest(new { error = error.Code, message = error.Message }));
        })
        .WithName("CreateCategory")
        .RequireAuthorization("Admin")
        .Produces<object>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest);

        // PUT /api/v1/categories/{id} (admin only)
        group.MapPut("/{id:guid}", async (Guid id, UpdateCategoryCommand command, IMediator mediator) =>
        {
            if (id != command.Id)
                return Results.BadRequest(new { error = "Validation.IdMismatch", message = "Route ID does not match command ID." });

            var result = await mediator.Send(command);

            return result.Match(
                () => Results.NoContent(),
                error => Results.BadRequest(new { error = error.Code, message = error.Message }));
        })
        .WithName("UpdateCategory")
        .RequireAuthorization("Admin")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status400BadRequest);

        // DELETE /api/v1/categories/{id} (admin only)
        group.MapDelete("/{id:guid}", async (Guid id, IMediator mediator) =>
        {
            var result = await mediator.Send(new DeleteCategoryCommand { Id = id });

            return result.Match(
                () => Results.NoContent(),
                error => Results.NotFound(new { error = error.Code, message = error.Message }));
        })
        .WithName("DeleteCategory")
        .RequireAuthorization("Admin")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound);
    }
}
