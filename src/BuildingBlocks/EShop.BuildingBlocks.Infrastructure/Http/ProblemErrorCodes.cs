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
    /// Gateway proxy guards. The value keeps its dotted prefix on purpose: these two were already
    /// on the wire as <c>{ "error": "Request.PayloadTooLarge" }</c> from the four
    /// <c>*ProxyGuardMiddleware</c> classes, and the migration to problem+json moves the envelope
    /// without changing what the discriminator means — same rule as the codes above.
    /// </summary>
    public const string PayloadTooLarge = "Request.PayloadTooLarge";

    /// <summary>
    /// The 502-to-503 rewrite in the proxy guards, which previously produced no body at all.
    /// New value, so it follows the dotted convention of its neighbour rather than the older
    /// unprefixed style.
    /// </summary>
    public const string UpstreamUnavailable = "Gateway.UpstreamUnavailable";

    /// <summary>
    /// A failure the gateway's traffic simulator injected rather than one that actually happened.
    /// Distinct on purpose: the simulated response has to be shaped like a real one for the
    /// simulation to be worth anything, so the error code is the only thing left that can tell an
    /// investigator the failure was synthetic.
    /// </summary>
    public const string SimulatedFailure = "Gateway.SimulatedFailure";

    /// <summary>
    /// The catch-all. Ordering and Payment previously emitted "InternalError" while Identity,
    /// Catalog, Basket and the gateway emitted "InternalServerError"; unification picks the
    /// latter. Nothing in the repo consumes this value.
    /// </summary>
    public const string InternalServerError = "InternalServerError";
}
