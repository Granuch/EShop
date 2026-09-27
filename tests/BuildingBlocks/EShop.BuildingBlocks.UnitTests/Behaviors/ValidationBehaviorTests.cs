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
/// For the <b>generic</b> <c>Result&lt;T&gt;</c> it is converted into a <see cref="FieldValidationError"/>
/// failure, which is why endpoints have to discriminate on it — one that maps every Result error to
/// a single status will report validation errors under the wrong one. For the <b>non-generic</b>
/// <c>Result</c>, and for any other response type, it throws instead and <c>AddCommon()</c> maps
/// that. Since frontend-contracts F-03 both paths carry the same code, <c>ValidationError</c>, and
/// the same camelCase field map; before it they were two shapes with two codes.
/// </para>
/// </summary>
[TestFixture]
public class ValidationBehaviorTests
{
    private const string ValidationFailedCode = "ValidationError";

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
        Assert.That(response.Error, Is.InstanceOf<FieldValidationError>(), "the field map rides on the error itself");
        var failure = (FieldValidationError)response.Error;
        Assert.That(failure.Errors.Keys, Is.EqualTo(new[] { "email" }), "keyed by the camelCase wire name");
        Assert.That(failure.Errors["email"], Is.EqualTo(new[] { "Email is required" }));
        Assert.That(failure.Message, Is.EqualTo("Email is required"), "detail is the messages, without a property prefix");
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
    /// The non-generic <c>Result</c> throws too: it is a <c>Result</c>, so the conversion above
    /// reads as though it applies, but the check is specifically <c>typeof(Result&lt;&gt;)</c>. Nine
    /// of Catalog's fourteen commands return this shape. It throws on purpose — many of those
    /// endpoints map every Result error to one status (a DELETE to 404), which would misreport a
    /// validation failure — and the exception carries the same camelCase map, so the client sees
    /// one shape either way.
    /// </summary>
    [Test]
    public async Task ThrowsInstead_WhenTheResponseIsTheNonGenericResult_WithTheSameFieldMap()
    {
        var behavior = new ValidationBehavior<NonGenericResultCommand, Result>(
            [new NonGenericResultValidator()],
            NullLogger<ValidationBehavior<NonGenericResultCommand, Result>>.Instance);

        var handlerRan = false;

        var thrown = Assert.ThrowsAsync<EShop.BuildingBlocks.Application.Exceptions.ValidationException>(
            async () => await behavior.Handle(
                new NonGenericResultCommand(string.Empty),
                _ =>
                {
                    handlerRan = true;
                    return Task.FromResult(Result.Success());
                },
                CancellationToken.None));

        Assert.That(thrown!.Errors.Keys, Is.EqualTo(new[] { "email" }));
        Assert.That(handlerRan, Is.False, "the handler must not run once validation has failed");
    }
}
