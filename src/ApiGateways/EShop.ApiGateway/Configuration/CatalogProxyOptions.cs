namespace EShop.ApiGateway.Configuration;

public sealed class CatalogProxyOptions
{
    public const string SectionName = "CatalogProxy";

    public long MaxRequestBodySizeBytes { get; set; } = 1_048_576;

    /// <summary>
    /// G8 (admin panel S16). The cap for <see cref="Middleware.CatalogProxyGuardMiddleware.LargeBodyPathPrefixes"/> —
    /// product import — in place of <see cref="MaxRequestBodySizeBytes"/>. 0 disables it, like the general cap.
    /// </summary>
    /// <remarks>
    /// Sized so the largest import Catalog accepts always fits: 1 000 rows (<c>ImportProductsCommand.MaxRows</c>), each
    /// up to 1 250 characters of name, description and SKU, which a .NET client's default JSON encoder escapes to six
    /// bytes a character when they are not ASCII — about 7.7 MB. A general cap this large would let any catalog write
    /// carry it, which is why it applies to the import path alone.
    /// </remarks>
    public long ImportMaxRequestBodySizeBytes { get; set; } = 8_388_608;
    public int UpstreamUnavailableRetryAfterSeconds { get; set; } = 5;
}
