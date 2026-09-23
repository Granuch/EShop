using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Auditing;
using MediatR;

namespace EShop.Catalog.Application.Administration.Commands.InvalidateCacheFamilies;

/// <summary>
/// Makes one of Catalog's cache families — or all of them — unreachable (admin panel S19, endpoint #88): the lever an
/// operator pulls after changing data behind the application's back, or when a list looks stale during an incident.
///
/// <para>
/// <b>Deliberately NOT an <c>ICacheInvalidatingCommand</c></b>, though that marker bumps families too. Its failure
/// posture is right for a write, whose cache eviction is a side effect of something already committed: a failed bump
/// is logged and the write still answers success. Here the bump is the whole command, so the same posture would answer
/// "invalidated" while Redis refused every bump. The handler bumps through <c>ICacheKeyVersionProvider</c> itself and
/// reports a failure as <c>Cache.Unavailable</c>. It is also why this is safe to do outside the behavior: the command
/// writes nothing to the database, so it is not transactional and there is no commit for a bump to precede.
/// </para>
/// </summary>
public sealed record InvalidateCacheFamiliesCommand : IRequest<Result<CacheInvalidationReport>>, IAuditedCommand
{
    string IAuditedCommand.AuditEntityType => "CacheFamily";

    // One row per family bumped, read from the report — so "who flushed products:list?" is answered by the
    // (EntityType, EntityId) index. A refused or failed request is one row with no entity, like any other command.
    string? IAuditedCommand.AuditEntityId => null;

    IReadOnlyList<AuditedItem>? IAuditedCommand.AuditItemsFromResult(object? value)
        => (value as CacheInvalidationReport)?.Families.Select(f => new AuditedItem(f)).ToList();

    /// <summary>One family from <see cref="CatalogCacheFamilies.All"/>, or <c>null</c> for every one of them.</summary>
    public string? Family { get; init; }
}

/// <summary>What was invalidated.</summary>
/// <param name="Service">The service whose cache this was: always <c>catalog</c>, so a client never reads it as global.</param>
/// <param name="Families">The families bumped, in order.</param>
public sealed record CacheInvalidationReport(string Service, IReadOnlyList<string> Families);
