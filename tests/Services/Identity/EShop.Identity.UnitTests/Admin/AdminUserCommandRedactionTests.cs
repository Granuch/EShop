using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.Identity.Application.Users.Commands.ChangeUserEmail;
using EShop.Identity.Application.Users.Commands.CreateUser;
using EShop.Identity.Application.Users.Commands.UpdateUserProfile;
using MediatR;
using Microsoft.Extensions.Logging;

namespace EShop.Identity.UnitTests.Admin;

/// <summary>
/// Admin panel S7 — that the new admin commands do not write the personal data they carry into the
/// log in clear text.
/// </summary>
/// <remarks>
/// <para>
/// <c>LoggingBehavior</c> logs the <b>whole</b> request object at Information with
/// <c>{@Request}</c>, and redacts a property only if it carries <c>[SensitiveData]</c> or its name
/// happens to be on a hardcoded list of nineteen. That list is all credentials —
/// <c>Password</c>, <c>Token</c>, <c>Otp</c> and so on — so an email address and a phone number
/// match nothing and are logged verbatim unless attributed. These commands are the first admin
/// surface to carry another person's contact details.
/// </para>
/// <para>
/// Driven through the real behavior rather than by reflecting for the attribute. An attribute
/// assertion passes if the attribute is present but <c>LoggingBehavior</c> stops honouring it;
/// this goes red either way. <c>CreateUserCommand.Password</c> is included even though its name is
/// on the list, because the list is a coincidence — renaming it to <c>InitialPassword</c> must not
/// silently start logging it, and that rename is exactly what this catches.
/// </para>
/// </remarks>
[TestFixture]
public class AdminUserCommandRedactionTests
{
    private const string Redacted = "****";

    /// <summary>Captures the structured state handed to ILogger, as the BuildingBlocks suite does.</summary>
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
            => Messages.Add(state?.ToString() ?? string.Empty);
    }

    private static async Task<string> LoggedTextAsync<TRequest>(TRequest request)
        where TRequest : IRequest<Result<Unit>>
    {
        var logger = new CapturingLogger<LoggingBehavior<TRequest, Result<Unit>>>();
        var behavior = new LoggingBehavior<TRequest, Result<Unit>>(logger);

        await behavior.Handle(request, _ => Task.FromResult(Result<Unit>.Success(Unit.Value)), CancellationToken.None);

        return string.Join("\n", logger.Messages);
    }

    [Test]
    public async Task ChangeUserEmail_DoesNotLogTheAddress()
    {
        var logged = await LoggedTextAsync(new ChangeUserEmailCommand
        {
            UserId = "user-1",
            Email = "secret-person@example.com"
        });

        Assert.That(logged, Does.Not.Contain("secret-person@example.com"));
        Assert.That(logged, Does.Contain(Redacted));
        Assert.That(logged, Does.Contain("user-1"), "the id is not personal data and stays loggable");
    }

    [Test]
    public async Task UpdateUserProfile_DoesNotLogThePhoneNumber()
    {
        var logged = await LoggedTextAsync(new UpdateUserProfileCommand
        {
            UserId = "user-1",
            FirstName = "Ada",
            PhoneNumber = "+44-7700-900123"
        });

        Assert.That(logged, Does.Not.Contain("+44-7700-900123"));
        Assert.That(logged, Does.Contain(Redacted));
    }

    [Test]
    public async Task CreateUser_DoesNotLogThePassword()
    {
        // Result<CreateUserResponse>, so it cannot go through the Unit helper above.
        var logger = new CapturingLogger<LoggingBehavior<CreateUserCommand, Result<CreateUserResponse>>>();
        var behavior = new LoggingBehavior<CreateUserCommand, Result<CreateUserResponse>>(logger);

        await behavior.Handle(
            new CreateUserCommand { Email = "new@test.com", Password = "hunter2-initial-password" },
            _ => Task.FromResult(Result<CreateUserResponse>.Success(new CreateUserResponse())),
            CancellationToken.None);

        var logged = string.Join("\n", logger.Messages);

        Assert.That(logged, Does.Not.Contain("hunter2-initial-password"));
        Assert.That(logged, Does.Contain(Redacted));
    }
}
