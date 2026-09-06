namespace EShop.Basket.Application.Abstractions;

public interface IBasketMetrics
{
    void RecordItemAdded(string source);
    void RecordCheckout(string status);
    void RecordPriceSyncUpdate(string status);
    void RecordOutboxRecovery(string status);
    IDisposable MeasureOperation(string operation);
}
