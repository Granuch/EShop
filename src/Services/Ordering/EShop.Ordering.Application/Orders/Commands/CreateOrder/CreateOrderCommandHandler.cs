using System.Diagnostics;
using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Domain;
using EShop.Ordering.Application.Telemetry;
using EShop.Ordering.Domain.Entities;
using EShop.Ordering.Domain.Interfaces;
using EShop.Ordering.Domain.ValueObjects;

namespace EShop.Ordering.Application.Orders.Commands.CreateOrder;

/// <summary>
/// Creates an order priced from Catalog. See <see cref="CatalogPricing"/>.
/// </summary>
public class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, Result<Guid>>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IProductCatalogReader _catalog;

    public CreateOrderCommandHandler(
        IOrderRepository orderRepository,
        IUnitOfWork unitOfWork,
        IProductCatalogReader catalog)
    {
        _orderRepository = orderRepository;
        _unitOfWork = unitOfWork;
        _catalog = catalog;
    }

    public async Task<Result<Guid>> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
    {
        using var activity = OrderingActivitySource.Source.StartActivity("Ordering.CreateOrder");
        activity?.SetTag("order.user_id", request.UserId);
        activity?.SetTag("order.item_count", request.Items.Count);

        // Built before pricing so a malformed address fails without a round trip per item.
        var address = new Address(
            request.Street,
            request.City,
            request.State,
            request.ZipCode,
            request.Country);

        var priced = await CatalogPricing.PriceAsync(
            _catalog,
            request.Items.Select(i => (i.ProductId, i.Quantity)),
            cancellationToken);

        if (priced.IsFailure)
        {
            activity?.SetStatus(ActivityStatusCode.Error, priced.Error!.Code);
            return Result<Guid>.Failure(priced.Error!);
        }

        var order = Order.Create(request.UserId, address, priced.Value!);

        await _orderRepository.AddAsync(order, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        activity?.SetTag("order.id", order.Id.ToString());
        activity?.SetTag("order.total", order.TotalPrice);

        return Result<Guid>.Success(order.Id);
    }
}
