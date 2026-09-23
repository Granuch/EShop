using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Caching;
using MediatR;
using Microsoft.Extensions.Logging;

namespace EShop.Catalog.Application.Administration.Commands.InvalidateCacheFamilies;

public sealed class InvalidateCacheFamiliesCommandHandler
    : IRequestHandler<InvalidateCacheFamiliesCommand, Result<CacheInvalidationReport>>
{
    public const string ServiceName = "catalog";
    public const string CacheUnavailableCode = "Cache.Unavailable";

    private readonly ICacheKeyVersionProvider _versions;
    private readonly ILogger<InvalidateCacheFamiliesCommandHandler> _logger;

    public InvalidateCacheFamiliesCommandHandler(
        ICacheKeyVersionProvider versions,
        ILogger<InvalidateCacheFamiliesCommandHandler> logger)
    {
        _versions = versions;
        _logger = logger;
    }

    public async Task<Result<CacheInvalidationReport>> Handle(
        InvalidateCacheFamiliesCommand request,
        CancellationToken cancellationToken)
    {
        // The validator has already refused an unknown name, so this is either one known family or all of them.
        var families = request.Family is null ? CatalogCacheFamilies.All : [request.Family];
        var bumped = new List<string>(families.Count);

        foreach (var family in families)
        {
            try
            {
                await _versions.BumpVersionAsync(family, cancellationToken);
                bumped.Add(family);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The exception text can name the Redis endpoint, so it goes to the log, not to the caller.
                _logger.LogError(ex, "Cache family {Family} could not be invalidated", family);

                var done = bumped.Count == 0 ? "No family was invalidated" : $"Invalidated {string.Join(", ", bumped)}";
                return Result<CacheInvalidationReport>.Failure(new Error(
                    CacheUnavailableCode,
                    $"{done}; '{family}' could not be invalidated because the cache is unavailable."));
            }
        }

        _logger.LogInformation("Invalidated cache families {Families}", string.Join(", ", bumped));
        return new CacheInvalidationReport(ServiceName, bumped);
    }
}
