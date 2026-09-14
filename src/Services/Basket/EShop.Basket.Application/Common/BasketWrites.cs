using EShop.BuildingBlocks.Application;
using MediatR;

namespace EShop.Basket.Application.Common;

/// <summary>
/// Basket audit S4 (H3). Every basket write is read → change → a save conditioned on what was read. If another write
/// landed in between — a second tab, a price sync, a checkout — the save writes nothing and the whole attempt runs
/// again on a fresh read, so the other write is kept and this one is applied on top of it. The basket used to be
/// saved last-write-wins, which silently dropped items and restored prices a price sync had just replaced.
/// </summary>
public static class BasketWrites
{
    /// <summary>
    /// Far more than any real contention on one customer's basket needs; past it the caller gets
    /// <see cref="BasketErrors.ConcurrentUpdate"/> (409) instead of looping.
    /// </summary>
    public const int MaxAttempts = 5;

    public static Result<Unit> Done => Result<Unit>.Success(Unit.Value);

    /// <summary>
    /// Runs <paramref name="attempt"/> until it returns a result. An attempt returns <c>null</c> when its conditional
    /// write lost a race; it must read the basket afresh every time, because that is what makes the retry correct.
    /// </summary>
    public static async Task<Result<Unit>> RunAsync(
        Func<CancellationToken, Task<Result<Unit>?>> attempt,
        CancellationToken cancellationToken)
    {
        for (var i = 0; i < MaxAttempts; i++)
        {
            if (await attempt(cancellationToken) is { } result)
            {
                return result;
            }
        }

        return Result<Unit>.Failure(BasketErrors.ConcurrentUpdate);
    }
}
