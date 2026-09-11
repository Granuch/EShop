using EShop.Catalog.Application.Categories;
using EShop.Catalog.Application.Categories.Commands.CreateCategory;
using EShop.BuildingBlocks.Infrastructure.Http;
using EShop.Catalog.Application.Categories.Commands.DeleteCategory;
using EShop.Catalog.Application.Categories.Commands.UpdateCategory;
using EShop.Catalog.Application.Categories.Queries.GetCategories;
using EShop.Catalog.Application.Categories.Queries.GetCategoryById;
using EShop.BuildingBlocks.Application.Pagination;
using EShop.Catalog.Application.Products.Queries.GetProductByCategory;
using EShop.Catalog.Application.Products.Queries.GetProducts;
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
                error => ProblemResults.For(error, StatusCodes.Status400BadRequest));
        })
        .WithName("GetCategories")
        .Produces<List<CategoryDto>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        // GET /api/v1/categories/{id}
        group.MapGet("/{id:guid}", async (Guid id, IMediator mediator) =>
        {
            var result = await mediator.Send(new GetCategoryByIdQuery { Id = id });

            return result.Match(
                value => Results.Ok(value),
                error => ProblemResults.For(error, StatusCodes.Status404NotFound));
        })
        .WithName("GetCategoryById")
        .Produces<CategoryDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // GET /api/v1/categories/{id}/products — paged like GET /api/v1/products (D5).
        // The paging parameters are nullable so they stay optional query-string parameters.
        group.MapGet("/{id:guid}/products", async (Guid id, int? pageNumber, int? pageSize, IMediator mediator) =>
        {
            var result = await mediator.Send(new GetProductByCategoryQuery
            {
                CategoryId = id,
                PageNumber = pageNumber,
                PageSize = pageSize
            });

            // Discriminates: an out-of-range page size is a Validation.Failed Result and owes a
            // 400. The blanket 404 mapping this replaced would have reported it as a missing category.
            return result.Match(
                value => Results.Ok(value),
                ProductEndpoints.ProblemForError);
        })
        .WithName("GetProductsByCategory")
        .Produces<PagedResult<ProductDto>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // POST /api/v1/categories (admin only)
        group.MapPost("/", async (CreateCategoryCommand command, IMediator mediator) =>
        {
            var result = await mediator.Send(command);

            return result.Match(
                value => Results.Created($"/api/v1/categories/{value}", new CreatedResourceResponse(value)),
                error => ProblemResults.For(error, StatusCodes.Status400BadRequest));
        })
        .WithName("CreateCategory")
        .RequireAuthorization("Admin")
        .Produces<CreatedResourceResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        // PUT /api/v1/categories/{id} (admin only)
        group.MapPut("/{id:guid}", async (Guid id, UpdateCategoryCommand command, IMediator mediator) =>
        {
            if (id != command.Id)
                return ProblemResults.For(
                    "Validation.IdMismatch",
                    "Route ID does not match command ID.",
                    StatusCodes.Status400BadRequest);

            var result = await mediator.Send(command);

            return result.Match(
                () => Results.NoContent(),
                error => ProblemResults.For(error, StatusCodes.Status400BadRequest));
        })
        .WithName("UpdateCategory")
        .RequireAuthorization("Admin")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        // DELETE /api/v1/categories/{id} (admin only)
        group.MapDelete("/{id:guid}", async (Guid id, IMediator mediator) =>
        {
            var result = await mediator.Send(new DeleteCategoryCommand { Id = id });

            return result.Match(
                () => Results.NoContent(),
                error => ProblemResults.For(error, StatusCodes.Status404NotFound));
        })
        .WithName("DeleteCategory")
        .RequireAuthorization("Admin")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
