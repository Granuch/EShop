using EShop.BuildingBlocks.Domain;

namespace EShop.Identity.Domain.Events;

/// <summary>
/// Event raised when user confirms email
/// </summary>
public record UserEmailConfirmedEvent : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredOn { get; } = DateTime.UtcNow;

    public string UserId { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
}
