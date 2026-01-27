# 📦 Phase 5: Ordering Service Implementation

**Duration**: 2 weeks  
**Team Size**: 2 developers  
**Prerequisites**: Phase 2 (Identity), Phase 4 (Basket) completed  
**Status**: 📋 Planning

---

## Objectives

- ✅ Order creation from basket checkout
- ✅ Order status management (state machine)
- ✅ Order history and tracking
- ✅ Integration with Payment Service
- ✅ Saga pattern for distributed transactions
- ✅ Event sourcing for order state changes
- ✅ Email notifications on order updates

---

## Domain Model

### Entities
- **Order**: Aggregate root
- **OrderItem**: Owned entity
- **OrderStatus**: Enum (Pending, Confirmed, Paid, Shipped, Delivered, Cancelled)

### Events
- OrderCreatedEvent
- OrderPaidEvent
- OrderShippedEvent
- OrderCancelledEvent

---

## Tasks Breakdown

### 5.1 Domain Layer

**Estimated Time**: 2 days

**Order Entity:**

```csharp
// EShop.Ordering.Domain/Entities/Order.cs

public class Order : AggregateRoot
{
    public string UserId { get; private set; } = string.Empty;
    public OrderStatus Status { get; private set; }
    public string ShippingAddress { get; private set; } = string.Empty;
    public string PaymentMethod { get; private set; } = string.Empty;
    public List<OrderItem> Items { get; private set; } = new();
    public decimal TotalAmount { get; private set; }
    public DateTime? PaidAt { get; private set; }
    public DateTime? ShippedAt { get; private set; }
    public DateTime? DeliveredAt { get; private set; }

    private Order() { }

    public static Order Create(
        string userId,
        string shippingAddress,
        string paymentMethod,
        List<OrderItem> items)
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Status = OrderStatus.Pending,
            ShippingAddress = shippingAddress,
            PaymentMethod = paymentMethod,
            Items = items,
            TotalAmount = items.Sum(i => i.Price * i.Quantity),
            CreatedAt = DateTime.UtcNow
        };

        order.AddDomainEvent(new OrderCreatedEvent(order.Id, order.UserId, order.TotalAmount));
        return order;
    }

    public void ConfirmPayment()
    {
        if (Status != OrderStatus.Pending)
            throw new DomainException("Only pending orders can be paid");

        Status = OrderStatus.Paid;
        PaidAt = DateTime.UtcNow;
        AddDomainEvent(new OrderPaidEvent(Id, UserId));
    }

    public void Ship()
    {
        if (Status != OrderStatus.Paid)
            throw new DomainException("Only paid orders can be shipped");

        Status = OrderStatus.Shipped;
        ShippedAt = DateTime.UtcNow;
        AddDomainEvent(new OrderShippedEvent(Id, UserId));
    }

    public void Cancel(string reason)
    {
        if (Status == OrderStatus.Shipped || Status == OrderStatus.Delivered)
            throw new DomainException("Cannot cancel shipped or delivered orders");

        Status = OrderStatus.Cancelled;
        AddDomainEvent(new OrderCancelledEvent(Id, UserId, reason));
    }
}

public class OrderItem
{
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Quantity { get; set; }
}

public enum OrderStatus
{
    Pending,
    Paid,
    Shipped,
    Delivered,
    Cancelled
}
```

---

### 5.2 Application Layer

**Estimated Time**: 3 days

**Create Order Command (from BasketCheckedOutEvent):**

```csharp
// EShop.Ordering.Application/Orders/Commands/CreateOrder/CreateOrderCommand.cs

public record CreateOrderCommand : IRequest<Result<Guid>>
{
    public string UserId { get; init; } = string.Empty;
    public string ShippingAddress { get; init; } = string.Empty;
    public string PaymentMethod { get; init; } = string.Empty;
    public List<CreateOrderItem> Items { get; init; } = new();
}

public record CreateOrderItem
{
    public Guid ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public int Quantity { get; init; }
}

public class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, Result<Guid>>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IPublishEndpoint _publishEndpoint;

    public async Task<Result<Guid>> Handle(
        CreateOrderCommand request,
        CancellationToken cancellationToken)
    {
        var orderItems = request.Items.Select(i => new OrderItem
        {
            ProductId = i.ProductId,
            ProductName = i.ProductName,
            Price = i.Price,
            Quantity = i.Quantity
        }).ToList();

        var order = Order.Create(
            userId: request.UserId,
            shippingAddress: request.ShippingAddress,
            paymentMethod: request.PaymentMethod,
            items: orderItems);

        await _orderRepository.AddAsync(order, cancellationToken);
        await _orderRepository.SaveChangesAsync(cancellationToken);

        // Publish event for Payment Service
        await _publishEndpoint.Publish(new ProcessPaymentCommand
        {
            OrderId = order.Id,
            Amount = order.TotalAmount,
            PaymentMethod = order.PaymentMethod
        }, cancellationToken);

        return Result<Guid>.Success(order.Id);
    }
}
```

**Get User Orders Query:**

```csharp
public record GetUserOrdersQuery : IRequest<PagedResult<OrderDto>>
{
    public string UserId { get; init; } = string.Empty;
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 10;
}

public class GetUserOrdersQueryHandler : IRequestHandler<GetUserOrdersQuery, PagedResult<OrderDto>>
{
    private readonly IOrderRepository _orderRepository;

    public async Task<PagedResult<OrderDto>> Handle(
        GetUserOrdersQuery request,
        CancellationToken cancellationToken)
    {
        var orders = await _orderRepository.GetUserOrdersAsync(
            request.UserId,
            request.Page,
            request.PageSize,
            cancellationToken);

        var totalCount = await _orderRepository.GetUserOrdersCountAsync(request.UserId);

        return new PagedResult<OrderDto>
        {
            Items = orders.Select(MapToDto).ToList(),
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = request.PageSize
        };
    }
}
```

---

### 5.3 Event Consumers

**Estimated Time**: 2 days

**BasketCheckedOutConsumer:**

```csharp
// EShop.Ordering.Application/Consumers/BasketCheckedOutConsumer.cs

public class BasketCheckedOutConsumer : IConsumer<BasketCheckedOutEvent>
{
    private readonly IMediator _mediator;

    public async Task Consume(ConsumeContext<BasketCheckedOutEvent> context)
    {
        var command = new CreateOrderCommand
        {
            UserId = context.Message.UserId,
            ShippingAddress = context.Message.ShippingAddress,
            PaymentMethod = context.Message.PaymentMethod,
            Items = context.Message.Items.Select(i => new CreateOrderItem
            {
                ProductId = i.ProductId,
                ProductName = i.ProductName,
                Price = i.Price,
                Quantity = i.Quantity
            }).ToList()
        };

        await _mediator.Send(command);
    }
}
```

**PaymentSuccessConsumer:**

```csharp
public class PaymentSuccessConsumer : IConsumer<PaymentSuccessEvent>
{
    private readonly IOrderRepository _orderRepository;

    public async Task Consume(ConsumeContext<PaymentSuccessEvent> context)
    {
        var order = await _orderRepository.GetByIdAsync(context.Message.OrderId);
        
        if (order != null)
        {
            order.ConfirmPayment();
            await _orderRepository.UpdateAsync(order);
            await _orderRepository.SaveChangesAsync();
        }
    }
}
```

---

### 5.4 API Layer

**Estimated Time**: 1 day

```csharp
[ApiController]
[Route("api/v{version:apiVersion}/orders")]
[Authorize]
public class OrdersController : ControllerBase
{
    private readonly IMediator _mediator;

    [HttpGet]
    public async Task<IActionResult> GetMyOrders([FromQuery] GetUserOrdersQuery query)
    {
        var userId = User.FindFirst("sub")?.Value!;
        query = query with { UserId = userId };
        
        var result = await _mediator.Send(query);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetOrder(Guid id)
    {
        var result = await _mediator.Send(new GetOrderByIdQuery { OrderId = id });
        return result.IsSuccess ? Ok(result.Value) : NotFound();
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> CancelOrder(Guid id, [FromBody] CancelOrderRequest request)
    {
        var command = new CancelOrderCommand { OrderId = id, Reason = request.Reason };
        await _mediator.Send(command);
        return NoContent();
    }
}
```

---

### 5.5 Saga Pattern (Order State Machine)

**Estimated Time**: 2 days

```csharp
public class OrderStateMachine : MassTransitStateMachine<OrderState>
{
    public OrderStateMachine()
    {
        InstanceState(x => x.CurrentState);

        Initially(
            When(OrderCreated)
                .Publish(context => new ProcessPaymentCommand
                {
                    OrderId = context.Saga.OrderId,
                    Amount = context.Message.TotalAmount
                })
                .TransitionTo(WaitingForPayment)
        );

        During(WaitingForPayment,
            When(PaymentSuccess)
                .TransitionTo(Paid),
            When(PaymentFailed)
                .Publish(context => new CancelOrderCommand { OrderId = context.Saga.OrderId })
                .TransitionTo(Cancelled)
        );
    }

    public State WaitingForPayment { get; private set; }
    public State Paid { get; private set; }
    public State Cancelled { get; private set; }

    public Event<OrderCreatedEvent> OrderCreated { get; private set; }
    public Event<PaymentSuccessEvent> PaymentSuccess { get; private set; }
    public Event<PaymentFailedEvent> PaymentFailed { get; private set; }
}
```

---

## Success Criteria

- [x] Orders created from basket checkout
- [x] Order status updates via events
- [x] Users can view order history
- [x] Saga handles payment flow
- [x] All tests passing (> 80% coverage)

---

## Next Phase

→ [Phase 6: Payment Service Implementation](phase-6-payment.md)

---

**Version**: 1.0  
**Last Updated**: 2024-01-15
