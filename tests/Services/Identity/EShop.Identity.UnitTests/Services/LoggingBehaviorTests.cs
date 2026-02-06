using System.Reflection;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Domain;
using MediatR;
using Microsoft.Extensions.Logging;
using Moq;

namespace EShop.Identity.UnitTests.Services;

[TestFixture]
public class LoggingBehaviorTests
{
    [Test]
    public void SafeLog_RedactsSensitiveProperties_ByAttributeAndName()
    {
        var behavior = new LoggingBehavior<TestRequest, string>(
            new Mock<ILogger<LoggingBehavior<TestRequest, string>>>().Object);

        var request = new TestRequest
        {
            Username = "user@example.com",
            Password = "Password123!",
            RefreshToken = "refresh-token",
            NewPassword = "NewPassword123!",
            Secret = "secret-value"
        };

        var safeLogMethod = typeof(LoggingBehavior<TestRequest, string>)
            .GetMethod("SafeLog", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.That(safeLogMethod, Is.Not.Null);

        var result = safeLogMethod!.Invoke(null, new object?[] { request });
        Assert.That(result, Is.Not.Null);

        var dict = result as IDictionary<string, object?>;
        Assert.That(dict, Is.Not.Null);

        Assert.That(dict![nameof(TestRequest.Username)], Is.EqualTo("user@example.com"));
        Assert.That(dict[nameof(TestRequest.Password)], Is.EqualTo("****"));
        Assert.That(dict[nameof(TestRequest.RefreshToken)], Is.EqualTo("****"));
        Assert.That(dict[nameof(TestRequest.NewPassword)], Is.EqualTo("****"));
        Assert.That(dict[nameof(TestRequest.Secret)], Is.EqualTo("****"));
    }

    public sealed record TestRequest : IRequest<string>
    {
        public string Username { get; init; } = string.Empty;

        public string Password { get; init; } = string.Empty;

        public string RefreshToken { get; init; } = string.Empty;

        [SensitiveData]
        public string NewPassword { get; init; } = string.Empty;

        public string Secret { get; init; } = string.Empty;
    }
}
