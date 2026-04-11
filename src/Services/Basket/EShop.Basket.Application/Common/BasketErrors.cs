using EShop.BuildingBlocks.Application;

namespace EShop.Basket.Application.Common;

public static class BasketErrors
{
    public static readonly Error BasketNotFound = new("Basket.NotFound", "Basket was not found.");
    public static readonly Error BasketEmpty = new("Basket.Empty", "Basket is empty.");
    public static readonly Error BasketPersistenceFailed = new("Basket.PersistenceFailed", "Basket data could not be persisted.");
    public static readonly Error BasketOperationFailed = new("Basket.OperationFailed", "Basket operation failed.");
    public static readonly Error CheckoutAlreadyInProgress = new("Basket.CheckoutInProgress", "Checkout is already in progress for this basket.");
    public static readonly Error ProductNotFound = new("Basket.ProductNotFound", "Product was not found.");
    public static readonly Error ProductVerificationFailed = new("Basket.ProductVerificationFailed", "Unable to verify product data at this time.");
}
