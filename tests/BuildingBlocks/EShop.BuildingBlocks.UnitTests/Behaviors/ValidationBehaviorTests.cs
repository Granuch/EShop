using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;

namespace EShop.BuildingBlocks.UnitTests.Behaviors;

/// <summary>
/// TEST-03. <see cref="ValidationBehavior{TRequest,TResponse}"/> is the first of the three ways a
/// request becomes an error status in this repo, and it is the one that surprises people: a
/// validation failure does <b>not</b> throw. It is converted into a <c>Result</c> failure carrying
/// the well-known code <c>Validation.Failed</c>, which is why endpoints have to discriminate on
/// that code — one that maps every Result error to a single status will report validation errors
/// under the wrong one.
/// </summary>
[TestFixture]
public class ValidationBehaviorTests
{
    private const string ValidationFailedCode = "Validation.Failed";

    private sealed record Command(string Email) : IRequest<Result<string>>;

    private sealed record PlainResponseCommand(string Email) : IRequest<string>;

    private sealed class CommandValidator : AbstractValidator<Command>
    {
        public CommandValidator() =>
            RuleFor(c => c.Email).NotEmpty().WithMessage("Email is required");
    }

    private sealed class PlainResponseValidator : AbstractValidator<PlainResponseCommand>
    {
        public PlainResponseValidator() =>
            RuleFor(c => c.Email).NotEmpty().WithMessage("Email is required");
    }

    [Test]
    public async Task ReturnsAFailureResult_RatherThanThrowing_WhenValidationFails()
    {
        var behavior = new ValidationBehavior<Command, Result<string>>(
            [new CommandValidator()],
            NullLogger<ValidationBehavior<Command, Result<string>>>.Instance);

        var handlerRan = false;

        var response = await behavior.Handle(
            new Command(string.Empty),
            _ =>
            {
                handlerRan = true;
                return Task.FromResult(Result<string>.Success("ok"));
            },
            CancellationToken.None);

        Assert.That(response.IsSuccess, Is.False);
        Assert.That(response.Error!.Code, Is.EqualTo(ValidationFailedCode),
            "endpoints discriminate on this exact code to choose 400 over their default status");
        Assert.That(response.Error.Message, Does.Contain("Email is required"));
        Assert.That(handlerRan, Is.False, "the handler must not run once validation has failed");
    }

    [Test]
    public async Task CallsTheHandler_WhenValidationPasses()
    {
        var behavior = new ValidationBehavior<Command, Result<string>>(
            [new CommandValidator()],
            NullLogger<ValidationBehavior<Command, Result<string>>>.Instance);

        var response = await behavior.Handle(
            new Command("user@test.com"),
            _ => Task.FromResult(Result<string>.Success("ok")),
            CancellationToken.None);

        Assert.That(response.IsSuccess, Is.True);
        Assert.That(response.Value, Is.EqualTo("ok"));
    }

    [Test]
    public async Task CallsTheHandler_WhenThereAreNoValidatorsAtAll()
    {
        var behavior = new ValidationBehavior<Command, Result<string>>(
            [],
            NullLogger<ValidationBehavior<Command, Result<string>>>.Instance);

        var response = await behavior.Handle(
            new Command(string.Empty),
            _ => Task.FromResult(Result<string>.Success("ok")),
            CancellationToken.None);

        Assert.That(response.IsSuccess, Is.True, "an unvalidated command is passed straight through");
    }

    /// <summary>
    /// The Result conversion only applies when the response is a <c>Result&lt;T&gt;</c>. Anything
    /// else falls back to throwing, which then reaches
    /// <c>ProblemDetailsExceptionMiddleware</c> — so the same validation failure produces a
    /// different code path depending purely on the handler's return type.
    /// </summary>
    [Test]
    public async Task ThrowsInstead_WhenTheResponseIsNotAResult()
    {
        var behavior = new ValidationBehavior<PlainResponseCommand, string>(
            [new PlainResponseValidator()],
            NullLogger<ValidationBehavior<PlainResponseCommand, string>>.Instance);

        Assert.That(
            async () => await behavior.Handle(
                new PlainResponseCommand(string.Empty),
                _ => Task.FromResult("ok"),
                CancellationToken.None),
            Throws.InstanceOf<EShop.BuildingBlocks.Application.Exceptions.ValidationException>());
    }
}
