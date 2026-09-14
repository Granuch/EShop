namespace EShop.Catalog.API.Endpoints;

/// <summary>
/// The body of every 201 this service returns: <c>{ "id": "…" }</c>.
///
/// <para>
/// L26 (Catalog audit Stage 10). The endpoints used to return an anonymous <c>new { id = value }</c>
/// declared as <c>.Produces&lt;object&gt;</c>, so the OpenAPI document described no schema for any
/// create. A named type serialises to the same JSON and gives the document something to describe.
/// </para>
/// </summary>
public sealed record CreatedResourceResponse(Guid Id);
