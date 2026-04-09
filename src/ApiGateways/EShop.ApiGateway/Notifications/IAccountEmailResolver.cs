namespace EShop.ApiGateway.Notifications;

public interface IAccountEmailResolver
{
    Task<string?> ResolveByUserIdAsync(string? userId, CancellationToken cancellationToken = default);
}
