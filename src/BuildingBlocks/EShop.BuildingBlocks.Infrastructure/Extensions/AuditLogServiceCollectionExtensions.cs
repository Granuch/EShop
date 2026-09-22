using EShop.BuildingBlocks.Infrastructure.Auditing;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.BuildingBlocks.Infrastructure.Extensions;

/// <summary>Registers the admin audit trail (admin panel S15, decision Q8a).</summary>
public static class AuditLogServiceCollectionExtensions
{
    /// <summary>
    /// <b>Call this FIRST in <c>Program.cs</c> — before <c>AddEShopCacheInvalidation()</c> where there is one, and
    /// before <c>Add&lt;Service&gt;Application()</c> everywhere.</b> MediatR runs behaviors in registration order,
    /// first registered outermost, and <see cref="AuditBehavior{TRequest,TResponse}"/> must be outermost to sit outside
    /// <c>TransactionBehavior</c> and see the outcome the caller got. Registered anywhere later it runs inside the
    /// transaction; its own-scope writer still commits, but a command that then rolls back leaves a
    /// <c>Succeeded</c> row for a change that never happened. Each service pins the position in a test.
    /// </summary>
    /// <param name="serviceName">The lowercase service name every row carries, and the gateway's name for it.</param>
    public static IServiceCollection AddEShopAuditLog<TDbContext>(this IServiceCollection services, string serviceName)
        where TDbContext : DbContext
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        services.AddSingleton(new AuditLogOptions(serviceName));
        services.AddSingleton<IAuditLogWriter, AuditLogWriter<TDbContext>>();
        services.AddScoped<IAuditLogReader, AuditLogReader<TDbContext>>();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(AuditBehavior<,>));

        return services;
    }
}
