using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;

namespace EShop.BuildingBlocks.UnitTests.Behaviors;

/// <summary>
/// TEST-03. <see cref="ValidationBehavior{TRequest,TResponse}"/> is the first of the three ways a
/// request becomes an error status in this repo, and it is the one that surprises people: whether
/// a validation failure throws is decided by the response type alone.
///
/// <para>
/// For the <b>generic</b> <c>Result&lt;T&gt;</c> it is converted into a failure carrying the
/// well-known code <c>Validation.Failed</c>, which is why endpoints have to discriminate on that
/// code — one that maps every Result error to a single status will report validation errors under
/// the wrong one. For the <b>non-generic</b> <c>Result</c>, and for any other response type, it
/// throws instead and <c>AddCommon()</c> maps that to a 400 whose errorCode is
/// <c>ValidationError</c>. The non-generic case is the trap: it looks like a Result, so the
/// discrimination above reads as though it covers it, and it never fires.
/// </para>
/// </summary>
[TestFixture]
public class ValidationBehaviorTests
{
    private const string ValidationFailedCode = "Validation.Failed";

    private sealed record Command(string Email) : IRequest<Result<string>>;

    private sealed record PlainResponseCommand(string Email) : IRequest<string>;

    private sealed record NonGenericResultCommand(string Email) : IRequest<Result>;

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

    private sealed class NonGenericResultValidator : AbstractValidator<NonGenericResultCommand>
    {
        public NonGenericResultValidator() =>
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

    /// <summary>
    /// The non-generic <c>Result</c> throws too, and this is the case worth pinning: it is a
    /// <c>Result</c>, so the conversion above reads as though it applies, but the check is
    /// specifically <c>typeof(Result&lt;&gt;)</c>. Nine of Catalog's fourteen commands return this
    /// shape — update, delete, publish, unpublish, both discount commands, remove/set-main image
    /// and both category writes — so for every one of them the <c>Validation.Failed</c>
    /// discrimination in <c>ProductEndpoints.ProblemForError</c> can never fire, and the client
    /// sees errorCode <c>ValidationError</c> instead. The status is 400 either way, which is
    /// exactly why nothing looked broken for so long.
    /// </summary>
    [Test]
    public async Task ThrowsInstead_WhenTheResponseIsTheNonGenericResult()
    {
        var behavior = new ValidationBehavior<NonGenericResultCommand, Result>(
            [new NonGenericResultValidator()],
            NullLogger<ValidationBehavior<NonGenericResultCommand, Result>>.Instance);

        var handlerRan = false;

        Assert.That(
            async () => await behavior.Handle(
                new NonGenericResultCommand(string.Empty),
                _ =>
                {
                    handlerRan = true;
                    return Task.FromResult(Result.Success());
                },
                CancellationToken.None),
            Throws.InstanceOf<EShop.BuildingBlocks.Application.Exceptions.ValidationException>());

        Assert.That(handlerRan, Is.False, "the handler must not run once validation has failed");
    }
}
