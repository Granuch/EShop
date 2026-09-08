using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Domain;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;

namespace EShop.BuildingBlocks.UnitTests.Behaviors;

/// <summary>
/// TEST-03. <see cref="LoggingBehavior{TRequest,TResponse}"/> writes every request to the log at
/// Information level, so whether a property is redacted decides whether secrets end up in plain
/// text in Seq.
///
/// <para>
/// Redaction happens by <b>either</b> the <c>[SensitiveData]</c> attribute <b>or</b> a hardcoded
/// list of 19 property names. The name list is the fragile half: it makes redaction a coincidence
/// of naming rather than a contract, so renaming <c>Code</c> to <c>OtpCode</c> starts logging OTPs
/// in clear text with nothing failing. These tests pin both mechanisms, and the last one pins the
/// gap itself so the weakness is visible rather than folklore.
/// </para>
/// </summary>
[TestFixture]
public class LoggingBehaviorRedactionTests
{
    private const string Redacted = "****";

    private sealed record CommandWithNamedSecrets : IRequest<Result<string>>
    {
        public string Email { get; init; } = "user@test.com";
        public string Password { get; init; } = "hunter2-password";
        public string Token { get; init; } = "hunter2-token";
    }

    private sealed record CommandWithAttributedSecret : IRequest<Result<string>>
    {
        public string Email { get; init; } = "user@test.com";

        [SensitiveData]
        public string ConfirmationValue { get; init; } = "hunter2-attributed";
    }

    private sealed record CommandWithUnlistedSecret : IRequest<Result<string>>
    {
        public string Email { get; init; } = "user@test.com";

        // Deliberately neither attributed nor on the name list.
        public string OtpCode { get; init; } = "hunter2-unlisted";
    }

    /// <summary>Captures the structured state passed to ILogger so tests can inspect it.</summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            // The request is logged with {@Request}; the destructured object's ToString is what a
            // non-structured sink would render, and it is enough to tell "****" from a real value.
            Messages.Add(state?.ToString() ?? string.Empty);
        }
    }

    private static async Task<List<string>> RunAsync<TRequest>(TRequest request)
        where TRequest : IRequest<Result<string>>
    {
        var logger = new CapturingLogger<LoggingBehavior<TRequest, Result<string>>>();
        var behavior = new LoggingBehavior<TRequest, Result<string>>(logger);

        await behavior.Handle(request, _ => Task.FromResult(Result<string>.Success("ok")), CancellationToken.None);

        return logger.Messages;
    }

    [Test]
    public async Task RedactsPropertiesMatchingTheHardcodedNameList()
    {
        var messages = await RunAsync(new CommandWithNamedSecrets());
        var all = string.Join("\n", messages);

        all.Should().NotContain("hunter2-password");
        all.Should().NotContain("hunter2-token");
        all.Should().Contain(Redacted);
        all.Should().Contain("user@test.com", "non-secret fields must still be logged");
    }

    [Test]
    public async Task RedactsPropertiesMarkedWithSensitiveData()
    {
        var messages = await RunAsync(new CommandWithAttributedSecret());
        var all = string.Join("\n", messages);

        all.Should().NotContain("hunter2-attributed");
        all.Should().Contain(Redacted);
    }

    /// <summary>
    /// The gap, pinned deliberately. A secret-bearing property that matches neither mechanism is
    /// logged in clear text at Information level and nothing fails. This test documents that
    /// rather than asserting it is fine: the fix for a new secret property is
    /// <c>[SensitiveData]</c>, never "hope the name happens to be on the list". If someone adds
    /// name-independent redaction, this test should change with it.
    /// </summary>
    [Test]
    public async Task DoesNotRedactASecretThatMatchesNeitherMechanism()
    {
        var messages = await RunAsync(new CommandWithUnlistedSecret());
        var all = string.Join("\n", messages);

        all.Should().Contain("hunter2-unlisted",
            "redaction is by attribute or hardcoded name only — this is why new secret properties need [SensitiveData]");
    }
}
