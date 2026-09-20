using EShop.BuildingBlocks.Application;
using EShop.Payment.Application.Payments.Common;
using EShop.Payment.Domain.Interfaces;
using MediatR;

namespace EShop.Payment.Application.Payments.Commands.ReplayFailedStripeWebhooks;

public sealed class ReplayFailedStripeWebhooksCommandHandler
    : IRequestHandler<ReplayFailedStripeWebhooksCommand, Result<FailedStripeWebhookReplayDto>>
{
    private readonly IFailedStripeWebhookReplayer _replayer;

    public ReplayFailedStripeWebhooksCommandHandler(IFailedStripeWebhookReplayer replayer)
    {
        _replayer = replayer;
    }

    public async Task<Result<FailedStripeWebhookReplayDto>> Handle(
        ReplayFailedStripeWebhooksCommand request,
        CancellationToken cancellationToken)
    {
        var report = await _replayer.ReplayAsync(
            request.EffectiveIds,
            ReplayFailedStripeWebhooksCommand.MaxReplayPerRequest,
            cancellationToken);

        // A row that failed again is reported, not thrown: the request itself succeeded, and the caller is told
        // exactly which captures are still outstanding. Throwing would lose the successes alongside it.
        return Result<FailedStripeWebhookReplayDto>.Success(report.ToDto());
    }
}
