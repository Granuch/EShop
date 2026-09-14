using System.Text.Json;
using EShop.Basket.Application.Commands.CheckoutBasket;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;
using Microsoft.Extensions.Logging;

namespace EShop.Basket.UnitTests.Application;

/// <summary>
/// Basket audit S10 (M5). The real <see cref="LoggingBehavior{TRequest,TResponse}"/> over a real checkout command: it logs
/// the whole request at Information level, and the shipping address matched none of its redacted names, so every checkout
/// wrote a home address to Seq and to log files kept for 30 days. The address is personal data and must not be in the log.
/// </summary>
[TestFixture]
public class CheckoutLoggingTests
{
    [Test]
    public async Task TheLoggedCheckoutRequest_DoesNotContainTheShippingAddress()
    {
        var logger = new CapturingLogger<LoggingBehavior<CheckoutBasketCommand, Result<Guid>>>();
        var behavior = new LoggingBehavior<CheckoutBasketCommand, Result<Guid>>(logger);
        var command = new CheckoutBasketCommand
        {
            UserId = "user-1",
            ShippingAddress = new CheckoutAddress
            {
                Street = "742 Evergreen Terrace",
                City = "Springfield",
                State = "OR",
                ZipCode = "97403",
                Country = "US"
            },
            PaymentMethod = "Card"
        };

        await behavior.Handle(command, _ => Task.FromResult(Result<Guid>.Success(Guid.NewGuid())), CancellationToken.None);

        var logged = JsonSerializer.Serialize(logger.Entries);
        Assert.That(logged, Does.Contain("user-1"), "the request itself is still logged");
        Assert.That(logged, Does.Not.Contain("Evergreen"));
        Assert.That(logged, Does.Not.Contain("97403"));
    }

    /// <summary>Every structured value each log call carried, the logged request object included.</summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<Dictionary<string, object?>> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var values = state as IEnumerable<KeyValuePair<string, object?>> ?? [];
            Entries.Add(values.ToDictionary(pair => pair.Key, pair => pair.Value));
        }
    }
}
