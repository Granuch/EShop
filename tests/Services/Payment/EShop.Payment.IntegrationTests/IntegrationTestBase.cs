using EShop.Payment.IntegrationTests.Fixtures;

namespace EShop.Payment.IntegrationTests;

[Category("Integration")]
public abstract class IntegrationTestBase : IDisposable
{
    protected PaymentApiFactory Factory { get; private set; } = null!;
    protected HttpClient Client { get; private set; } = null!;

    [SetUp]
    public virtual Task SetUpAsync()
    {
        Factory = CreateFactory();
        Client = Factory.CreateClient();
        return Task.CompletedTask;
    }

    [TearDown]
    public virtual Task TearDownAsync()
    {
        return Task.CompletedTask;
    }

    protected virtual PaymentApiFactory CreateFactory() => new();

    public void Dispose()
    {
        Client?.Dispose();
        Factory?.Dispose();
        GC.SuppressFinalize(this);
    }
}
