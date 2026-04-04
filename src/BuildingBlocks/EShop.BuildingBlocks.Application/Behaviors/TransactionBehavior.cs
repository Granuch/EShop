using EShop.BuildingBlocks.Domain;
using MediatR;
using Microsoft.Extensions.Logging;

namespace EShop.BuildingBlocks.Application.Behaviors;

/// <summary>
/// Marker interface for commands that require transactional execution.
/// Apply this to commands that modify data across multiple aggregates
/// or require atomic commit semantics.
/// </summary>
public interface ITransactionalCommand { }

/// <summary>
/// MediatR pipeline behavior that wraps command handlers in a database transaction.
/// 
/// Key features:
/// - Only activates for commands marked with ITransactionalCommand
/// - Uses IUnitOfWork for transaction management
/// - Supports nested transactions (won't start a new one if already active)
/// - Automatic rollback on exceptions
/// - Works correctly with the Outbox pattern
/// 
/// Order matters: This should run BEFORE ValidationBehavior in the pipeline
/// so validation errors don't cause unnecessary transaction overhead.
/// </summary>
public class TransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<TransactionBehavior<TRequest, TResponse>> _logger;

    public TransactionBehavior(
        IUnitOfWork unitOfWork,
        ILogger<TransactionBehavior<TRequest, TResponse>> logger)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // Only wrap in transaction if the request implements ITransactionalCommand
        if (request is not ITransactionalCommand)
        {
            return await next();
        }

        // Nested transaction guard: if upstream flow (for example idempotent consumer)
        // already opened a transaction, do not wrap again.
        if (_unitOfWork.HasActiveTransaction)
        {
            return await next();
        }

        var requestName = typeof(TRequest).Name;
        var requestId = Guid.NewGuid().ToString()[..8];

        _logger.LogDebug(
            "[{RequestId}] Beginning transaction for {RequestName}",
            requestId, requestName);

        try
        {
            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            var response = await next();

            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            _logger.LogDebug(
                "[{RequestId}] Transaction committed for {RequestName}",
                requestId, requestName);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[{RequestId}] Transaction rolled back for {RequestName}: {Error}",
                requestId, requestName, ex.Message);

            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }
}

/// <summary>
/// Alternative transaction behavior that wraps ALL commands (not just marked ones).
/// Use this if you want transactional semantics for all write operations by default.
/// 
/// Warning: This has higher overhead. Use only if your application truly needs
/// transactions for every command.
/// </summary>
public class AutoTransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<AutoTransactionBehavior<TRequest, TResponse>> _logger;

    public AutoTransactionBehavior(
        IUnitOfWork unitOfWork,
        ILogger<AutoTransactionBehavior<TRequest, TResponse>> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // Skip for queries (naming convention: queries end with "Query")
        var requestName = typeof(TRequest).Name;
        if (requestName.EndsWith("Query", StringComparison.OrdinalIgnoreCase))
        {
            return await next();
        }

        if (_unitOfWork.HasActiveTransaction)
        {
            return await next();
        }

        var requestId = Guid.NewGuid().ToString()[..8];

        _logger.LogDebug(
            "[{RequestId}] Auto-beginning transaction for {RequestName}",
            requestId, requestName);

        try
        {
            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            var response = await next();

            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            _logger.LogDebug(
                "[{RequestId}] Auto-transaction committed for {RequestName}",
                requestId, requestName);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[{RequestId}] Auto-transaction rolled back for {RequestName}: {Error}",
                requestId, requestName, ex.Message);

            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }
}
