using EShop.BuildingBlocks.Application;

namespace EShop.Basket.Application.Common;

public static class BasketErrors
{
    public static readonly Error BasketNotFound = new("Basket.NotFound", "Basket was not found.");
    public static readonly Error BasketEmpty = new("Basket.Empty", "Basket is empty.");
    public static readonly Error BasketPersistenceFailed = new("Basket.PersistenceFailed", "Basket data could not be persisted.");
    public static readonly Error BasketOperationFailed = new("Basket.OperationFailed", "Basket operation failed.");
}
