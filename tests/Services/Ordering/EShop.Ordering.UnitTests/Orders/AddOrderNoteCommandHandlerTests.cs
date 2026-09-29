using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Domain;
using EShop.Ordering.Application.Orders.Commands.AddOrderNote;
using EShop.Ordering.Domain.Entities;
using EShop.Ordering.Domain.Interfaces;
using EShop.Ordering.Domain.ValueObjects;
using Moq;

namespace EShop.Ordering.UnitTests.Orders;

/// <summary>
/// Admin panel S9, endpoint #60. The handler's whole job is the two things the aggregate cannot do
/// for itself: find out whether the order exists, and count notes it never loads.
/// </summary>
[TestFixture]
public class AddOrderNoteCommandHandlerTests
{
    private Mock<IOrderRepository> _orderRepositoryMock = null!;
    private Mock<IUnitOfWork> _unitOfWorkMock = null!;
    private Mock<ICurrentUserContext> _currentUserMock = null!;
    private AddOrderNoteCommandHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _orderRepositoryMock = new Mock<IOrderRepository>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _currentUserMock = new Mock<ICurrentUserContext>();

        _unitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _currentUserMock.SetupGet(x => x.UserId).Returns("admin-1");
        _currentUserMock.SetupGet(x => x.UserName).Returns("Admin One");

        _handler = new AddOrderNoteCommandHandler(
            _orderRepositoryMock.Object, _unitOfWorkMock.Object, _currentUserMock.Object);
    }

    private static readonly Address SeededAddress = new("123 Test St", "TestCity", "TS", "12345", "US");

    private Order Given()
    {
        var order = Order.Create("user-7", SeededAddress,
            [new OrderItem(Guid.NewGuid(), "Widget A", 10.00m, 2)]);

        _orderRepositoryMock
            .Setup(x => x.GetByIdAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        return order;
    }

    [Test]
    public async Task Handle_AttachesTheNote_AndSaves()
    {
        var order = Given();

        var result = await _handler.Handle(
            new AddOrderNoteCommand { OrderId = order.Id, Body = "called the customer" }, default);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(order.Notes, Has.Count.EqualTo(1));
        Assert.That(result.Value, Is.EqualTo(order.Notes.Single().Id));
        _unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// The author comes from the authenticated principal, never from the command — there is no field
    /// on <see cref="AddOrderNoteCommand"/> that could carry one.
    /// </summary>
    [Test]
    public async Task Handle_SignsTheNote_WithTheAuthenticatedCaller()
    {
        var order = Given();

        await _handler.Handle(new AddOrderNoteCommand { OrderId = order.Id, Body = "body" }, default);

        var note = order.Notes.Single();
        Assert.That(note.AuthorId, Is.EqualTo("admin-1"));
        Assert.That(note.AuthorName, Is.EqualTo("Admin One"));
    }

    /// <summary>A principal with no display-name claim still produces a note that names someone.</summary>
    [Test]
    public async Task Handle_WithNoDisplayName_FallsBackToTheUserId()
    {
        var order = Given();
        _currentUserMock.SetupGet(x => x.UserName).Returns((string?)null);

        await _handler.Handle(new AddOrderNoteCommand { OrderId = order.Id, Body = "body" }, default);

        Assert.That(order.Notes.Single().AuthorName, Is.EqualTo("admin-1"));
    }

    [Test]
    public async Task Handle_WithAMissingOrder_ReturnsNotFound_AndSavesNothing()
    {
        _orderRepositoryMock
            .Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Order?)null);

        var result = await _handler.Handle(
            new AddOrderNoteCommand { OrderId = Guid.NewGuid(), Body = "body" }, default);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error.Code, Is.EqualTo("Order.NotFound"));
        _unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// The cap is a <c>COUNT</c> pre-check, not an aggregate rule, because <c>Order._notes</c> is never
    /// loaded — an aggregate-side check would see zero on every persisted order.
    /// </summary>
    [Test]
    public async Task Handle_WhenTheOrderIsAtTheNoteLimit_IsRefused_AndSavesNothing()
    {
        var order = Given();
        _orderRepositoryMock
            .Setup(x => x.CountNotesAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Order.MaxNotes);

        var result = await _handler.Handle(
            new AddOrderNoteCommand { OrderId = order.Id, Body = "one too many" }, default);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error.Code, Is.EqualTo("Order.NoteLimitReached"));
        Assert.That(order.Notes, Is.Empty, "the aggregate must not be touched on a refused write");
        _unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Handle_OneBelowTheNoteLimit_IsAccepted()
    {
        var order = Given();
        _orderRepositoryMock
            .Setup(x => x.CountNotesAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Order.MaxNotes - 1);

        var result = await _handler.Handle(
            new AddOrderNoteCommand { OrderId = order.Id, Body = "the last one" }, default);

        Assert.That(result.IsSuccess, Is.True);
    }

    /// <summary>
    /// The count is taken for the order named in the request, not for whatever the repository was last
    /// asked about — a cap keyed to the wrong order would be no cap at all.
    /// </summary>
    [Test]
    public async Task Handle_CountsTheNotesOfTheOrderBeingAnnotated()
    {
        var order = Given();

        await _handler.Handle(new AddOrderNoteCommand { OrderId = order.Id, Body = "body" }, default);

        _orderRepositoryMock.Verify(
            x => x.CountNotesAsync(order.Id, It.IsAny<CancellationToken>()), Times.Once);
    }
}
