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
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
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

        // GET /api/v1/categories/{id} — the category with its whole live subtree (F-37)
        group.MapGet("/{id:guid}", async (Guid id, IMediator mediator) =>
        {
            var result = await mediator.Send(new GetCategoryByIdQuery { Id = id });

            // F-40: the all-zero id is a ValidationError and owes a 400; the blanket 404 this
            // replaced reported it as a missing category.
            return result.Match(
                value => Results.Ok(value),
                CategoryProblem);
        })
        .WithName("GetCategoryById")
        .Produces<CategoryDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
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

            // Discriminates: an out-of-range page size is a ValidationError Result and owes a
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

            // F-40: an unknown category is a 404 (it answered 400 Category.NotFound); F-39: a slug
            // another live sibling holds is a 409, as on move and restore.
            return result.Match(
                () => Results.NoContent(),
                CategoryProblem);
        })
        .WithName("UpdateCategory")
        .RequireAuthorization("Admin")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        // DELETE /api/v1/categories/{id} (admin only)
        group.MapDelete("/{id:guid}", async (Guid id, IMediator mediator) =>
        {
            var result = await mediator.Send(new DeleteCategoryCommand { Id = id });

            // F-40: Category.HasChildren / Category.HasProducts are 409 - the category exists and
            // its state blocks the delete. Both were 404 under the blanket mapping this replaced, so
            // a client reading a 404 on DELETE as "already gone" reported a delete that never ran.
            return result.Match(
                () => Results.NoContent(),
                CategoryProblem);
        })
        .WithName("DeleteCategory")
        .RequireAuthorization("Admin")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        // PUT /api/v1/categories/{id}/parent (admin only)
        group.MapPut("/{id:guid}/parent", async (Guid id, MoveCategoryRequest request, IMediator mediator) =>
        {
            // A dedicated request body rather than binding the command: null is a meaningful value
            // here (it promotes the category to a root), so "omitted" and "explicitly null" must not
            // be the same thing. A missing body fails binding (400 MalformedRequest), and since F-38
            // a body without the property is a 400 ValidationError keyed newParentCategoryId - `{}`
            // used to bind as null and silently make the category a root.
            if (!request.HasNewParentCategoryId)
                return ProblemResults.For(
                    FieldValidationError.For(
                        MoveCategoryRequest.NewParentCategoryIdWireName,
                        "newParentCategoryId is required: send a category id, or null to make this a root category"),
                    StatusCodes.Status400BadRequest);

            var result = await mediator.Send(new MoveCategoryCommand
            {
                CategoryId = id,
                NewParentCategoryId = request.NewParentCategoryId
            });

            return result.Match(
                () => Results.NoContent(),
                CategoryProblem);
        })
        .WithName("MoveCategory")
        .RequireAuthorization("Admin")
        .Produces(StatusCodes.Status204NoContent)
        // 400 covers both a malformed move (cycle, self-parent) AND a missing NEW parent: that one
        // is `Category.ParentNotFound`, which does not match ProblemForError's ".NotFound" suffix
        // rule — the character before "NotFound" is the "t" of "Parent". The near-miss is real but
        // the status is right, and it matches what CreateCategory already answers for that code.
        // 404 is reserved for the category the ROUTE names. 409 is a slug another live category
        // already holds at the target level (F-39).
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        // POST /api/v1/categories/{id}/restore (admin only)
        group.MapPost("/{id:guid}/restore", async (Guid id, IMediator mediator) =>
        {
            var result = await mediator.Send(new RestoreCategoryCommand { CategoryId = id });

            return result.Match(
                () => Results.NoContent(),
                CategoryProblem);
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
    /// The status mapping for every category endpoint that names a category in its route (F-40,
    /// frontend-contracts R5): read, update, delete, move and restore. Each used to pick its own —
    /// a blanket 404 on read and delete, a blanket 400 on update — so business refusals read as
    /// "not found" and a missing category read as a bad request.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><b>409</b> when another resource's state blocks a request that is itself fine:
    /// <c>Category.SlugConflict</c> (a live sibling holds the slug — on update, move and restore;
    /// Admin panel S5 made restore's a 409 first) and <c>Category.HasChildren</c> /
    /// <c>Category.HasProducts</c> on delete. Re-sending the same request succeeds once that state
    /// changes.</item>
    /// <item><b>404</b> for a <c>*.NotFound</c> code, which in these handlers only ever names the
    /// route's category. <c>Category.ParentNotFound</c> deliberately does not match (the character
    /// before <c>NotFound</c> is the <c>t</c> of <c>Parent</c>): the request body points at a
    /// missing parent, which is a 400, as on create.</item>
    /// <item><b>400</b> for everything else — validation, a cycle, restoring a live category.</item>
    /// </list>
    /// Create keeps its own blanket 400, so its pre-checked slug conflict stays a 400 as before.
    /// </remarks>
    internal static IResult CategoryProblem(Error error)
        => ProblemResults.For(
            error,
            error.Code switch
            {
                "Category.SlugConflict" or "Category.HasChildren" or "Category.HasProducts"
                    => StatusCodes.Status409Conflict,
                var code when code.EndsWith(".NotFound", StringComparison.Ordinal) => StatusCodes.Status404NotFound,
                _ => StatusCodes.Status400BadRequest
            });
}

/// <summary>
/// The body of <c>PUT /api/v1/categories/{id}/parent</c> (Admin panel S5).
/// </summary>
/// <remarks>
/// <para>
/// A one-field body rather than binding <c>MoveCategoryCommand</c> directly: the route owns the
/// category id, and <c>null</c> for the parent means "make this a root" rather than "leave it
/// alone", so the property has to be genuinely present in the body.
/// </para>
/// <para>
/// F-38 (frontend-contracts R5): that used to be only a comment. As a positional record an omitted
/// property bound exactly like an explicit <c>null</c>, so <c>{}</c> re-rooted the category. The
/// setter now records that System.Text.Json assigned it — which it does for an explicit
/// <c>null</c> and never for an omitted property — and the endpoint refuses the body when it did
/// not. <c>[JsonRequired]</c> would refuse it too, but as a <c>MalformedRequest</c> whose detail
/// names the JSON path <c>$</c> rather than the missing field.
/// </para>
/// </remarks>
public sealed class MoveCategoryRequest
{
    /// <summary>The camelCase name the client sends, and the key of the 400 when it is missing.</summary>
    public const string NewParentCategoryIdWireName = "newParentCategoryId";

    private readonly Guid? _newParentCategoryId;

    /// <summary>The new parent, or <c>null</c> to make the category a root. Must be present.</summary>
    /// <remarks>
    /// <c>[Required]</c> is for the OpenAPI document only: it lists the property as required, as the
    /// positional record this replaced did. Catalog registers no minimal-API validation, so it changes
    /// no request handling — the presence check above is what refuses <c>{}</c>.
    /// </remarks>
    [Required]
    public Guid? NewParentCategoryId
    {
        get => _newParentCategoryId;
        init
        {
            _newParentCategoryId = value;
            HasNewParentCategoryId = true;
        }
    }

    /// <summary>Whether the body carried the property at all. Not part of the wire contract.</summary>
    [JsonIgnore]
    public bool HasNewParentCategoryId { get; private init; }
}
