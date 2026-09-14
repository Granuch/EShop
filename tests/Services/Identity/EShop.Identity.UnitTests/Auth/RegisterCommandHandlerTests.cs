using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Messaging;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Identity.Application.Auth.Commands.Register;
using EShop.Identity.Domain.Entities;
using EShop.Identity.Domain.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace EShop.Identity.UnitTests.Auth;

/// <summary>
/// TEST-06. <c>RegisterCommandHandler</c> owns the service's only dual-write: it creates a user
/// and announces that creation to five other services. Nothing tested it.
///
/// <para>
/// The property that matters is negative — <b>no failure path may enqueue an event</b>. The
/// outbox row is written inside <c>TransactionBehavior</c>'s ambient transaction, so an event
/// enqueued alongside a rolled-back user creation would be discarded with it; but an event
/// enqueued on a path that <i>returns a failure instead of throwing</i> is committed, because
/// <c>TransactionBehavior</c> rolls back only in its <c>catch</c>. Catalog, Basket, Ordering,
/// Payment and Notification would then each provision for a user that does not exist.
/// </para>
/// </summary>
[TestFixture]
public class RegisterCommandHandlerTests
{
    private const string Email = "new@test.com";
    private const string Password = "New@123456";
    private const string CorrelationId = "corr-abc123";

    private Mock<UserManager<ApplicationUser>> _userManagerMock = null!;
    private Mock<IUserRepository> _userRepositoryMock = null!;
    private Mock<IIntegrationEventOutbox> _outboxMock = null!;
    private Mock<ICurrentUserContext> _currentUserContextMock = null!;
    private RegisterCommandHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _userManagerMock = MockUserManager();
        _userRepositoryMock = new Mock<IUserRepository>();
        _outboxMock = new Mock<IIntegrationEventOutbox>();
        _currentUserContextMock = new Mock<ICurrentUserContext>();
        _currentUserContextMock.SetupGet(x => x.CorrelationId).Returns(CorrelationId);

        // Default: the email is free.
        _userRepositoryMock
            .Setup(x => x.GetByEmailAsync(Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApplicationUser?)null);

        _handler = new RegisterCommandHandler(
            _userManagerMock.Object,
            _userRepositoryMock.Object,
            _outboxMock.Object,
            _currentUserContextMock.Object,
            new Mock<ILogger<RegisterCommandHandler>>().Object);
    }

    private static RegisterCommand Command() => new()
    {
        Email = Email,
        Password = Password,
        FirstName = "New",
        LastName = "Person"
    };

    /// <summary>
    /// Arranges a successful creation. <c>CreateAsync</c> is what assigns the id in production, so
    /// the callback mirrors that — without it the handler would announce an empty UserId.
    /// </summary>
    private void ArrangeSuccessfulCreate(string userId = "user-1")
    {
        _userManagerMock
            .Setup(x => x.CreateAsync(It.IsAny<ApplicationUser>(), Password))
            .Callback<ApplicationUser, string>((u, _) => u.Id = userId)
            .ReturnsAsync(IdentityResult.Success);

        _userManagerMock
            .Setup(x => x.AddToRoleAsync(It.IsAny<ApplicationUser>(), "User"))
            .ReturnsAsync(IdentityResult.Success);
    }

    // ---------------------------------------------------------------------------------------
    // No failure path may announce a user
    // ---------------------------------------------------------------------------------------

    [Test]
    public async Task Handle_WithAnAlreadyRegisteredEmail_CreatesNothingAndAnnouncesNothing()
    {
        _userRepositoryMock
            .Setup(x => x.GetByEmailAsync(Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApplicationUser { Id = "existing", Email = Email });

        var result = await _handler.Handle(Command(), CancellationToken.None);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error!.Code, Is.EqualTo("Auth.EmailExists"));
        _userManagerMock.Verify(
            x => x.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()), Times.Never);
        _outboxMock.Verify(
            x => x.Enqueue(It.IsAny<IIntegrationEvent>(), It.IsAny<string>()), Times.Never);
    }

    /// <summary>
    /// The dual-write guard. This path returns a <c>Result</c> failure rather than throwing, so
    /// <c>TransactionBehavior</c> commits it — an event enqueued here would survive and tell five
    /// services to provision for a user that was never created.
    /// </summary>
    [Test]
    public async Task Handle_WhenUserCreationFails_AnnouncesNothing()
    {
        _userManagerMock
            .Setup(x => x.CreateAsync(It.IsAny<ApplicationUser>(), Password))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Password too weak" }));

        var result = await _handler.Handle(Command(), CancellationToken.None);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error!.Code, Is.EqualTo("Auth.CreateFailed"));
        _outboxMock.Verify(
            x => x.Enqueue(It.IsAny<IIntegrationEvent>(), It.IsAny<string>()), Times.Never,
            "TransactionBehavior commits a Result failure, so an event enqueued here would be delivered");
    }

    /// <summary>The reason belongs in the response; ASP.NET Identity's own text is what a caller needs.</summary>
    [Test]
    public async Task Handle_WhenUserCreationFails_SurfacesTheIdentityErrors()
    {
        _userManagerMock
            .Setup(x => x.CreateAsync(It.IsAny<ApplicationUser>(), Password))
            .ReturnsAsync(IdentityResult.Failed(
                new IdentityError { Description = "Password too weak" },
                new IdentityError { Description = "Email already taken" }));

        var result = await _handler.Handle(Command(), CancellationToken.None);

        Assert.That(result.Error!.Message, Does.Contain("Password too weak"));
        Assert.That(result.Error.Message, Does.Contain("Email already taken"));
    }

    // ---------------------------------------------------------------------------------------
    // The success path announces exactly once, with a usable payload
    // ---------------------------------------------------------------------------------------

    [Test]
    public async Task Handle_OnSuccess_EnqueuesExactlyOneEventCarryingTheNewUserId()
    {
        ArrangeSuccessfulCreate("user-42");
        UserRegisteredIntegrationEvent? captured = null;
        _outboxMock
            .Setup(x => x.Enqueue(It.IsAny<IIntegrationEvent>(), It.IsAny<string>()))
            .Callback<IIntegrationEvent, string?>((e, _) => captured = e as UserRegisteredIntegrationEvent);

        var result = await _handler.Handle(Command(), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.UserId, Is.EqualTo("user-42"));
        _outboxMock.Verify(
            x => x.Enqueue(It.IsAny<IIntegrationEvent>(), It.IsAny<string>()), Times.Once);
        Assert.That(captured, Is.Not.Null, "the event must be a UserRegisteredIntegrationEvent");
        Assert.That(captured!.UserId, Is.EqualTo("user-42"),
            "an event announcing an empty id is worse than no event — consumers cannot resolve it");
    }

    /// <summary>
    /// The correlation id is the only thread tying a registration to whatever the five consuming
    /// services then do with it. Dropping it costs nothing at runtime and everything in an incident.
    /// </summary>
    [Test]
    public async Task Handle_OnSuccess_PropagatesTheCorrelationId()
    {
        ArrangeSuccessfulCreate();

        await _handler.Handle(Command(), CancellationToken.None);

        _outboxMock.Verify(x => x.Enqueue(It.IsAny<IIntegrationEvent>(), CorrelationId), Times.Once);
    }

    /// <summary>
    /// The event deliberately carries no email or name — see its own doc comment. Consumers that
    /// need details call the Identity API. Re-adding PII here would put it in RabbitMQ and, via
    /// the outbox, in the database for the seven days OutboxCleanupService retains a row.
    /// </summary>
    [Test]
    public async Task Handle_OnSuccess_AnnouncesNoPersonalData()
    {
        ArrangeSuccessfulCreate();
        UserRegisteredIntegrationEvent? captured = null;
        _outboxMock
            .Setup(x => x.Enqueue(It.IsAny<IIntegrationEvent>(), It.IsAny<string>()))
            .Callback<IIntegrationEvent, string?>((e, _) => captured = e as UserRegisteredIntegrationEvent);

        await _handler.Handle(Command(), CancellationToken.None);

        var payload = System.Text.Json.JsonSerializer.Serialize(captured);
        Assert.That(payload, Does.Not.Contain(Email));
        Assert.That(payload, Does.Not.Contain("New"), "no first name in the payload either");
    }

    [Test]
    public async Task Handle_OnSuccess_CreatesTheUserUnconfirmedAndActive()
    {
        ApplicationUser? created = null;
        _userManagerMock
            .Setup(x => x.CreateAsync(It.IsAny<ApplicationUser>(), Password))
            .Callback<ApplicationUser, string>((u, _) => { u.Id = "user-1"; created = u; })
            .ReturnsAsync(IdentityResult.Success);
        _userManagerMock
            .Setup(x => x.AddToRoleAsync(It.IsAny<ApplicationUser>(), "User"))
            .ReturnsAsync(IdentityResult.Success);

        await _handler.Handle(Command(), CancellationToken.None);

        Assert.That(created, Is.Not.Null);
        Assert.That(created!.EmailConfirmed, Is.False, "confirmation is the point of the follow-up flow");
        Assert.That(created.IsActive, Is.True, "IsActive defaults to true; its setter is private");
        Assert.That(created.UserName, Is.EqualTo(Email), "login is by email, so UserName must mirror it");
    }

    /// <summary>
    /// Pins today's behaviour rather than endorsing it: a failed default-role assignment is logged
    /// and registration still succeeds, so the account exists with no role. That is survivable
    /// (authorization denies rather than grants) but it is a silent partial success, and it is the
    /// kind of thing that should change deliberately rather than by accident — hence the test.
    /// </summary>
    [Test]
    public async Task Handle_WhenTheDefaultRoleCannotBeAssigned_StillSucceedsAndStillAnnounces()
    {
        _userManagerMock
            .Setup(x => x.CreateAsync(It.IsAny<ApplicationUser>(), Password))
            .Callback<ApplicationUser, string>((u, _) => u.Id = "user-1")
            .ReturnsAsync(IdentityResult.Success);
        _userManagerMock
            .Setup(x => x.AddToRoleAsync(It.IsAny<ApplicationUser>(), "User"))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Role does not exist" }));

        var result = await _handler.Handle(Command(), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        _outboxMock.Verify(
            x => x.Enqueue(It.IsAny<IIntegrationEvent>(), It.IsAny<string>()), Times.Once);
    }

    private static Mock<UserManager<ApplicationUser>> MockUserManager()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        var optionsAccessor = new Mock<IOptions<IdentityOptions>>();
        optionsAccessor.Setup(x => x.Value).Returns(new IdentityOptions());

        return new Mock<UserManager<ApplicationUser>>(
            store.Object,
            optionsAccessor.Object,
            null!, null!, null!, null!, null!, null!, null!);
    }
}
