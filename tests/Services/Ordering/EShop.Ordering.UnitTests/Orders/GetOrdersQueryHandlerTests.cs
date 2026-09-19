using EShop.Ordering.Application.Abstractions;
using EShop.Ordering.Application.Orders.Queries;
using EShop.Ordering.Application.Orders.Queries.GetOrders;
using EShop.Ordering.Domain.Entities;
using Moq;

namespace EShop.Ordering.UnitTests.Orders;

/// <summary>
/// The handler's whole job is translating a bound query into an <see cref="OrderListFilter"/>, so
/// these assert the filter it hands the query service rather than the page it hands back.
///
/// <para>
/// Admin panel S8 replaced <c>GetOrdersAsync(status, page, size, ct)</c> with the filter record. The
/// three tests that stood here set up the old signature and had to be rewritten: a new parameter
/// before the trailing <c>CancellationToken</c> breaks every Moq setup with a CS1503 naming the
/// cancellation token, which is exactly the churn the record avoids from here on.
/// </para>
/// </summary>
[TestFixture]
public class GetOrdersQueryHandlerTests
{
    private Mock<IOrderQueryService> _orderQueryServiceMock = null!;
    private GetOrdersQueryHandler _handler = null!;
    private OrderListFilter? _capturedFilter;
    private OrderSortBy _capturedSortBy;
    private bool _capturedIsDescending;
    private int _capturedPageNumber;
    private int _capturedPageSize;

    [SetUp]
    public void SetUp()
    {
        _capturedFilter = null;
        _orderQueryServiceMock = new Mock<IOrderQueryService>();
        _orderQueryServiceMock
            .Setup(x => x.GetOrdersAsync(
                It.IsAny<OrderListFilter>(),
                It.IsAny<OrderSortBy>(),
                It.IsAny<bool>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Callback<OrderListFilter, OrderSortBy, bool, int, int, CancellationToken>(
                (filter, sortBy, isDescending, pageNumber, pageSize, _) =>
                {
                    _capturedFilter = filter;
                    _capturedSortBy = sortBy;
                    _capturedIsDescending = isDescending;
                    _capturedPageNumber = pageNumber;
                    _capturedPageSize = pageSize;
                })
            .ReturnsAsync((new List<OrderDto>(), 0));

        _handler = new GetOrdersQueryHandler(_orderQueryServiceMock.Object);
    }

    [Test]
    public async Task Handle_WithDefaultQuery_ShouldReturnPagedResult()
    {
        var dtos = new List<OrderDto>
        {
            new() { Id = Guid.NewGuid(), UserId = "user-1", TotalPrice = 20.00m, Status = OrderStatus.Pending }
        };
        _orderQueryServiceMock
            .Setup(x => x.GetOrdersAsync(
                It.IsAny<OrderListFilter>(), It.IsAny<OrderSortBy>(), It.IsAny<bool>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((dtos, 1));

        var result = await _handler.Handle(new GetOrdersQuery(), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Items.ToList(), Has.Count.EqualTo(1));
        Assert.That(result.Value.TotalCount, Is.EqualTo(1));
    }

    [Test]
    public async Task Handle_WithNoFilters_AsksForEveryStatus_NewestFirst_OnPageOne()
    {
        await _handler.Handle(new GetOrdersQuery(), CancellationToken.None);

        Assert.That(_capturedFilter!.Statuses, Is.Empty, "an empty set means every status, not none");
        Assert.That(_capturedFilter.Search, Is.Null);
        Assert.That(_capturedSortBy, Is.EqualTo(OrderSortBy.CreatedAt));
        Assert.That(_capturedIsDescending, Is.True);
        Assert.That(_capturedPageNumber, Is.EqualTo(1));
        Assert.That(_capturedPageSize, Is.EqualTo(10));
    }

    [Test]
    public async Task Handle_WithStatusFilter_ShouldPassStatusToQueryService()
    {
        await _handler.Handle(new GetOrdersQuery { Status = "Paid" }, CancellationToken.None);

        Assert.That(_capturedFilter!.Statuses, Is.EquivalentTo(new[] { OrderStatus.Paid }));
    }

    /// <summary>The single legacy parameter and the repeatable one are a union, not rivals.</summary>
    [Test]
    public async Task Handle_WithBothStatusAndStatuses_UnionsThem_WithoutRepeating()
    {
        await _handler.Handle(
            new GetOrdersQuery { Status = "paid", Statuses = ["Shipped", "PAID"] },
            CancellationToken.None);

        Assert.That(_capturedFilter!.Statuses,
            Is.EquivalentTo(new[] { OrderStatus.Shipped, OrderStatus.Paid }));
    }

    [Test]
    public async Task Handle_WithEveryFilter_PassesThemAllThrough()
    {
        var from = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

        await _handler.Handle(
            new GetOrdersQuery
            {
                Search = "  pi_abc  ",
                From = from,
                To = to,
                MinTotal = 5m,
                MaxTotal = 500m,
                SortBy = "totalprice",
                IsDescending = false,
                PageNumber = 3,
                PageSize = 25
            },
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(_capturedFilter!.Search, Is.EqualTo("  pi_abc  "), "trimming belongs to the query service");
            Assert.That(_capturedFilter.From, Is.EqualTo(from));
            Assert.That(_capturedFilter.To, Is.EqualTo(to));
            Assert.That(_capturedFilter.MinTotal, Is.EqualTo(5m));
            Assert.That(_capturedFilter.MaxTotal, Is.EqualTo(500m));
            Assert.That(_capturedSortBy, Is.EqualTo(OrderSortBy.TotalPrice));
            Assert.That(_capturedIsDescending, Is.False);
            Assert.That(_capturedPageNumber, Is.EqualTo(3));
            Assert.That(_capturedPageSize, Is.EqualTo(25));
        });
    }

    /// <summary>
    /// <c>?from=2026-01-01</c> binds to a DateTime with <c>Kind.Unspecified</c>, and Npgsql refuses to
    /// send one as a <c>timestamp with time zone</c> parameter — so without this the most obvious
    /// possible date filter is a 500 rather than a filter.
    /// </summary>
    [Test]
    public async Task Handle_WithADateCarryingNoTimeZone_ReadsItAsUtc()
    {
        var unspecified = new DateTime(2026, 1, 1, 9, 30, 0, DateTimeKind.Unspecified);

        await _handler.Handle(new GetOrdersQuery { From = unspecified }, CancellationToken.None);

        Assert.That(_capturedFilter!.From!.Value.Kind, Is.EqualTo(DateTimeKind.Utc));
        Assert.That(_capturedFilter.From!.Value, Is.EqualTo(unspecified));
    }

    [Test]
    public async Task Handle_WithALocalDate_ConvertsItToUtc()
    {
        var local = new DateTime(2026, 1, 1, 9, 30, 0, DateTimeKind.Local);

        await _handler.Handle(new GetOrdersQuery { To = local }, CancellationToken.None);

        Assert.That(_capturedFilter!.To!.Value.Kind, Is.EqualTo(DateTimeKind.Utc));
        Assert.That(_capturedFilter.To!.Value, Is.EqualTo(local.ToUniversalTime()));
    }

    [Test]
    public async Task Handle_EmptyResult_ShouldReturnEmptyPagedResult()
    {
        var result = await _handler.Handle(new GetOrdersQuery(), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Items, Is.Empty);
        Assert.That(result.Value.TotalCount, Is.EqualTo(0));
    }
}
