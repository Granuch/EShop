using FluentValidation;

namespace EShop.Payment.Application.Payments.Commands.ReplayFailedStripeWebhooks;

/// <summary>
/// Risk A8: a bulk endpoint needs an explicit bound, and it has to be an error rather than a silent truncation — a
/// caller who sent 500 ids and got 100 replayed has no way to know which 400 were ignored.
/// </summary>
public sealed class ReplayFailedStripeWebhooksCommandValidator
    : AbstractValidator<ReplayFailedStripeWebhooksCommand>
{
    public ReplayFailedStripeWebhooksCommandValidator()
    {
        RuleFor(x => x.EffectiveIds)
            .Must(ids => ids.Count <= ReplayFailedStripeWebhooksCommand.MaxReplayPerRequest)
            .WithMessage($"At most {ReplayFailedStripeWebhooksCommand.MaxReplayPerRequest} webhooks can be replayed in one request.")
            .OverridePropertyName(nameof(ReplayFailedStripeWebhooksCommand.Ids));

        RuleFor(x => x.EffectiveIds)
            .Must(ids => ids.All(id => id != Guid.Empty))
            .WithMessage("A capture id must not be empty.")
            .OverridePropertyName(nameof(ReplayFailedStripeWebhooksCommand.Ids));
    }
}
