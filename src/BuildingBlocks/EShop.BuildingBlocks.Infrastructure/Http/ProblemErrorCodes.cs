namespace EShop.BuildingBlocks.Infrastructure.Http;

/// <summary>
/// Machine-readable discriminators emitted as the <c>errorCode</c> extension member on
/// exception-path problem responses. These are the tokens the per-service
/// GlobalExceptionHandlerMiddleware implementations previously put in the non-standard
/// <c>type</c> field, carried over verbatim so the wire values did not change meaning.
/// </summary>
public static class ProblemErrorCodes
{
    public const string ValidationError = "ValidationError";
    public const string NotFound = "NotFound";
    public const string DomainError = "DomainError";
    public const string Unauthorized = "Unauthorized";
    public const string ConcurrencyConflict = "ConcurrencyConflict";
    public const string DuplicateResource = "DuplicateResource";
    public const string PersistenceConflict = "PersistenceConflict";
    public const string MalformedRequest = "MalformedRequest";

    /// <summary>
    /// The catch-all. Ordering and Payment previously emitted "InternalError" while Identity,
    /// Catalog, Basket and the gateway emitted "InternalServerError"; unification picks the
    /// latter. Nothing in the repo consumes this value.
    /// </summary>
    public const string InternalServerError = "InternalServerError";
}
