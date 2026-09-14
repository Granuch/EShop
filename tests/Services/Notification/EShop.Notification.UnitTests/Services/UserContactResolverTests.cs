using System.Net;
using System.Text;
using EShop.Notification.Application.Abstractions;
using EShop.Notification.Infrastructure.Configuration;
using EShop.Notification.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EShop.Notification.UnitTests.Services;

/// <summary>
/// Notification audit S3 (M13, D3, L20). The resolver had no tests, and every answer — a deleted user, a wrong API key,
/// an outage — came back as the same <c>null</c>. Each branch is pinned here against a scripted handler; retry delays are
/// zero.
/// </summary>
[TestFixture]
public class UserContactResolverTests
{
    [Test]
    public async Task AContactWithAnEmail_IsFound_WithTheUsersName()
    {
        var handler = new ScriptedHandler(Json(HttpStatusCode.OK, """{"email":"ada@test.com","firstName":"Ada","lastName":"Lovelace"}"""));

        var lookup = await Resolver(handler).ResolveAsync("user 1");

        Assert.Multiple(() =>
        {
            Assert.That(lookup.Recipient?.Email, Is.EqualTo("ada@test.com"));
            Assert.That(lookup.Recipient?.DisplayName, Is.EqualTo("Ada Lovelace"));
            Assert.That(handler.Calls, Is.EqualTo(1));
            Assert.That(handler.LastPath, Is.EqualTo("/api/v1/users/user%201/contact"));
        });
    }

    [TestCase(HttpStatusCode.NotFound, "Identity has no such user (404).")]
    [TestCase(HttpStatusCode.BadRequest, "Identity rejected the user id (400).")]
    public async Task APermanentAnswer_IsUndeliverable_AndNotRetried(HttpStatusCode status, string reason)
    {
        var handler = new ScriptedHandler(Status(status));

        var lookup = await Resolver(handler).ResolveAsync("user-1");

        Assert.Multiple(() =>
        {
            Assert.That(lookup.Recipient, Is.Null);
            Assert.That(lookup.UndeliverableReason, Is.EqualTo(reason));
            Assert.That(handler.Calls, Is.EqualTo(1));
        });
    }

    [TestCase("""{"email":""}""")]
    [TestCase("""{"email":"not-an-address"}""")]
    public async Task AContactWithoutAUsableEmail_IsUndeliverable(string body)
    {
        var handler = new ScriptedHandler(Json(HttpStatusCode.OK, body));

        var lookup = await Resolver(handler).ResolveAsync("user-1");

        Assert.Multiple(() =>
        {
            Assert.That(lookup.Recipient, Is.Null);
            Assert.That(lookup.UndeliverableReason, Is.Not.Null);
            Assert.That(handler.Calls, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ABlankUserId_IsUndeliverable_WithoutACall()
    {
        var handler = new ScriptedHandler(Status(HttpStatusCode.OK));

        var lookup = await Resolver(handler).ResolveAsync("  ");

        Assert.Multiple(() =>
        {
            Assert.That(lookup.Recipient, Is.Null);
            Assert.That(handler.Calls, Is.Zero);
        });
    }

    [TestCase(HttpStatusCode.Unauthorized)]
    [TestCase(HttpStatusCode.Forbidden)]
    public void ARefusedApiKey_IsAConfigurationError_AndNotRetriedHere(HttpStatusCode status)
    {
        var handler = new ScriptedHandler(Status(status));

        var thrown = Assert.ThrowsAsync<UserContactUnavailableException>(() => Resolver(handler).ResolveAsync("user-1"));

        Assert.Multiple(() =>
        {
            Assert.That(thrown!.IsConfigurationError, Is.True);
            Assert.That(handler.Calls, Is.EqualTo(1));
        });
    }

    [TestCase(HttpStatusCode.ServiceUnavailable)]
    [TestCase(HttpStatusCode.InternalServerError)]
    [TestCase(HttpStatusCode.TooManyRequests)]
    [TestCase(HttpStatusCode.RequestTimeout)]
    public void ATransientAnswer_IsRetried_ThenReportedUnavailable(HttpStatusCode status)
    {
        var handler = new ScriptedHandler(Status(status));

        var thrown = Assert.ThrowsAsync<UserContactUnavailableException>(() => Resolver(handler).ResolveAsync("user-1"));

        Assert.Multiple(() =>
        {
            Assert.That(thrown!.IsConfigurationError, Is.False);
            Assert.That(handler.Calls, Is.EqualTo(UserContactResolver.MaxAttempts));
        });
    }

    [Test]
    public async Task ATransientAnswerThenSuccess_IsFound()
    {
        var handler = new ScriptedHandler(
            Status(HttpStatusCode.ServiceUnavailable),
            Json(HttpStatusCode.OK, """{"email":"ada@test.com"}"""));

        var lookup = await Resolver(handler).ResolveAsync("user-1");

        Assert.Multiple(() =>
        {
            Assert.That(lookup.Recipient?.Email, Is.EqualTo("ada@test.com"));
            Assert.That(handler.Calls, Is.EqualTo(2));
        });
    }

    [Test]
    public void AnUnreachableIdentity_IsRetried_ThenReportedUnavailable()
    {
        var handler = new ScriptedHandler(() => throw new HttpRequestException("Connection refused"));

        var thrown = Assert.ThrowsAsync<UserContactUnavailableException>(() => Resolver(handler).ResolveAsync("user-1"));

        Assert.Multiple(() =>
        {
            Assert.That(thrown!.InnerException, Is.InstanceOf<HttpRequestException>());
            Assert.That(handler.Calls, Is.EqualTo(UserContactResolver.MaxAttempts));
        });
    }

    /// <summary>L20. HttpClient.Timeout surfaces as TaskCanceledException, which the old retry loop did not catch.</summary>
    [Test]
    public void ATimeout_IsRetried_ThenReportedUnavailable()
    {
        var handler = new ScriptedHandler(() => throw new TaskCanceledException("The request timed out."));

        var thrown = Assert.ThrowsAsync<UserContactUnavailableException>(() => Resolver(handler).ResolveAsync("user-1"));

        Assert.Multiple(() =>
        {
            Assert.That(thrown!.IsConfigurationError, Is.False);
            Assert.That(handler.Calls, Is.EqualTo(UserContactResolver.MaxAttempts));
        });
    }

    /// <summary>L20. An unreadable body used to escape as a JsonException.</summary>
    [Test]
    public void AnUnreadableBody_IsRetried_ThenReportedUnavailable()
    {
        var handler = new ScriptedHandler(Json(HttpStatusCode.OK, "not json"));

        Assert.ThrowsAsync<UserContactUnavailableException>(() => Resolver(handler).ResolveAsync("user-1"));
        Assert.That(handler.Calls, Is.EqualTo(UserContactResolver.MaxAttempts));
    }

    [Test]
    public void TheCallersCancellation_Propagates_AndIsNotReportedAsAnOutage()
    {
        var handler = new ScriptedHandler(Status(HttpStatusCode.OK));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.CatchAsync<OperationCanceledException>(() => Resolver(handler).ResolveAsync("user-1", cancelled.Token));
        Assert.That(handler.Calls, Is.Zero);
    }

    private static UserContactResolver Resolver(ScriptedHandler handler)
        => new(
            new HttpClient(handler) { BaseAddress = new Uri("http://identity/") },
            Options.Create(new IdentityServiceSettings { RetryBaseDelayMilliseconds = 0 }),
            NullLogger<UserContactResolver>.Instance);

    private static Func<HttpResponseMessage> Status(HttpStatusCode status) => () => new HttpResponseMessage(status);

    private static Func<HttpResponseMessage> Json(HttpStatusCode status, string body)
        => () => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    /// <summary>Answers each call with the next step of the script; the last step repeats.</summary>
    private sealed class ScriptedHandler(params Func<HttpResponseMessage>[] script) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? LastPath { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastPath = request.RequestUri?.AbsolutePath;
            var step = script[Math.Min(Calls, script.Length - 1)];
            Calls++;
            return Task.FromResult(step());
        }
    }
}
