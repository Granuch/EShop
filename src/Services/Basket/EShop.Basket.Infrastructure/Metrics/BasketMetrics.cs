using EShop.Basket.Application.Abstractions;
using Prometheus;

namespace EShop.Basket.Infrastructure.Metrics;

public class BasketMetrics : IBasketMetrics
{
    private static readonly Counter BasketItemsAddedTotal = Prometheus.Metrics.CreateCounter(
        "basket_items_added_total",
        "Total number of basket items added",
        new CounterConfiguration
        {
            LabelNames = ["source"]
        });

    private static readonly Counter BasketCheckoutsTotal = Prometheus.Metrics.CreateCounter(
        "basket_checkouts_total",
        "Total number of basket checkout attempts",
        new CounterConfiguration
        {
            LabelNames = ["status"]
        });

    private static readonly Counter BasketPriceSyncUpdatesTotal = Prometheus.Metrics.CreateCounter(
        "basket_price_sync_updates_total",
        "Total number of basket price synchronization updates",
        new CounterConfiguration
        {
            LabelNames = ["status"]
        });

    private static readonly Histogram BasketOperationDurationSeconds = Prometheus.Metrics.CreateHistogram(
        "basket_operation_duration_seconds",
        "Duration of basket operations",
        new HistogramConfiguration
        {
            LabelNames = ["operation"],
            Buckets = Histogram.ExponentialBuckets(0.001, 2, 12)
        });

    public void RecordItemAdded(string source) => BasketItemsAddedTotal.WithLabels(source).Inc();

    public void RecordCheckout(string status) => BasketCheckoutsTotal.WithLabels(status).Inc();

    public void RecordPriceSyncUpdate(string status) => BasketPriceSyncUpdatesTotal.WithLabels(status).Inc();

    public IDisposable MeasureOperation(string operation) => BasketOperationDurationSeconds.WithLabels(operation).NewTimer();
}
