using EShop.Catalog.Application.Categories;
using EShop.Catalog.Application.Categories.Commands.CreateCategory;
using EShop.BuildingBlocks.Infrastructure.Http;
using EShop.BuildingBlocks.Application;
using EShop.Catalog.Application.Categories.Commands.DeleteCategory;
using EShop.Catalog.Application.Categories.Commands.MoveCategory;
using EShop.Catalog.Application.Categories.Commands.ReorderCategories;
using EShop.Catalog.Application.Categories.Commands.RestoreCategory;
using EShop.Catalog.Application.Categories.Commands.UpdateCategory;
using EShop.Catalog.Application.Categories.Queries.GetCategories;
using EShop.Catalog.Application.Categories.Queries.GetCategoryById;
using EShop.Catalog.Application.Categories.Queries.GetCategoryStats;
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

        // PUT /api/v1/categories/reorder (admin only)
        //
        // Declared before /{id:guid} for readability; the :guid constraint means "reorder" could
        // not bind as an id anyway.
        group.MapPut("/reorder", async (ReorderCategoriesCommand command, IMediator mediator) =>
        {
            var result = await mediator.Send(command);

            return result.Match(
                () => Results.NoContent(),
                ProductEndpoints.ProblemForError);
        })
        .WithName("ReorderCategories")
        .RequireAuthorization("Admin")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // GET /api/v1/categories (hierarchical structure), ?includeInactive=true for admins
        group.MapGet("/", async (HttpContext http, IMediator mediator) =>
        {
            // A4 / D1. The flag is set HERE, from the caller's role, and any bound value is
            // ignored — the query is sent constructed rather than bound precisely so there is no
            // client-settable property to overwrite. It is part of the cache key, so getting this
            // wrong would let one admin request poison the entry every anonymous caller reads.
            var result = await mediator.Send(new GetCategoriesQuery
            {
                IncludeInactive = CanSeeInactive(http)
            });

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
        group.MapGet("/{id:guid}/products", async (Guid id, int? pageNumber, int? pageSize, HttpContext http, IMediator mediator) =>
        {
            var result = await mediator.Send(new GetProductByCategoryQuery
            {
                CategoryId = id,
                PageNumber = pageNumber,
                PageSize = pageSize,
                // D1 / H5a (S5, #54). Same rule as GET /products: set from the role, never bound.
                // Part of the cache key, so an admin's draft-bearing page cannot be served to an
                // anonymous caller.
                IncludeUnpublished = CanSeeUnpublished(http)
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

        // PUT /api/v1/categories/{id}/parent (admin only)
        group.MapPut("/{id:guid}/parent", async (Guid id, MoveCategoryRequest request, IMediator mediator) =>
        {
            // A dedicated request body rather than binding the command: null is a meaningful value
            // here (it promotes the category to a root), so "omitted" and "explicitly null" must not
            // be the same thing. A missing body fails binding and answers 400 instead of silently
            // making the category a root.
            var result = await mediator.Send(new MoveCategoryCommand
            {
                CategoryId = id,
                NewParentCategoryId = request.NewParentCategoryId
            });

            return result.Match(
                () => Results.NoContent(),
                ProductEndpoints.ProblemForError);
        })
        .WithName("MoveCategory")
        .RequireAuthorization("Admin")
        .Produces(StatusCodes.Status204NoContent)
        // 400 covers both a malformed move (cycle, self-parent) AND a missing NEW parent: that one
        // is `Category.ParentNotFound`, which does not match ProblemForError's ".NotFound" suffix
        // rule — the character before "NotFound" is the "t" of "Parent". The near-miss is real but
        // the status is right, and it matches what CreateCategory already answers for that code.
        // 404 is reserved for the category the ROUTE names.
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // POST /api/v1/categories/{id}/restore (admin only)
        group.MapPost("/{id:guid}/restore", async (Guid id, IMediator mediator) =>
        {
            var result = await mediator.Send(new RestoreCategoryCommand { CategoryId = id });

            return result.Match(
                () => Results.NoContent(),
                RestoreProblem);
        })
        .WithName("RestoreCategory")
        .RequireAuthorization("Admin")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        // GET /api/v1/categories/{id}/stats (admin only)
        group.MapGet("/{id:guid}/stats", async (Guid id, IMediator mediator) =>
        {
            var result = await mediator.Send(new GetCategoryStatsQuery { CategoryId = id });

            return result.Match(
                value => Results.Ok(value),
                ProductEndpoints.ProblemForError);
        })
        .WithName("GetCategoryStats")
        .RequireAuthorization("Admin")
        .Produces<CategoryStatsDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }

    /// <summary>
    /// A4 (Admin panel S5). Whether this caller may see deactivated categories — admins only.
    /// The single place the decision is made, and made from the authenticated principal rather than
    /// from anything the client can send. The read endpoint is anonymous, so an unauthenticated
    /// request simply yields false.
    /// </summary>
    private static bool CanSeeInactive(HttpContext http) => http.User.IsInRole("Admin");

    /// <summary>
    /// D1 / H5a. Mirrors <c>ProductEndpoints.CanSeeUnpublished</c>, which is private to that class;
    /// duplicated rather than shared because the two are one line each and a shared helper would
    /// invite a future caller to pass a flag instead of a principal.
    /// </summary>
    private static bool CanSeeUnpublished(HttpContext http) => http.User.IsInRole("Admin");

    /// <summary>
    /// Restore's status mapping (Admin panel S5): a slug conflict is a <b>409</b>, not a 400,
    /// because nothing about the request is wrong — another category took the slug while this one
    /// was deleted, and resolving that makes the same request succeed. Mirrors
    /// <c>ProductEndpoints.RestoreProblem</c>, and matches what <c>AddCategorySlugConflict()</c>
    /// answers when the index catches the same collision instead of the handler's pre-check.
    /// </summary>
    private static IResult RestoreProblem(Error error)
        => ProblemResults.For(
            error,
            error.Code switch
            {
                "Category.SlugConflict" => StatusCodes.Status409Conflict,
                var code when code.EndsWith(".NotFound", StringComparison.Ordinal) => StatusCodes.Status404NotFound,
                _ => StatusCodes.Status400BadRequest
            });
}

/// <summary>
/// The body of <c>PUT /api/v1/categories/{id}/parent</c> (Admin panel S5).
/// </summary>
/// <remarks>
/// A one-field record rather than binding <c>MoveCategoryCommand</c> directly: the route owns the
/// category id, and <c>null</c> for the parent means "make this a root" rather than "leave it
/// alone", so the property has to be genuinely present in the body.
/// </remarks>
public sealed record MoveCategoryRequest(Guid? NewParentCategoryId);
