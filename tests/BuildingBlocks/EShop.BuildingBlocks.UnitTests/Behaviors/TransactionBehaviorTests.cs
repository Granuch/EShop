using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Domain;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EShop.BuildingBlocks.UnitTests.Behaviors;

/// <summary>
/// TEST-03. <see cref="TransactionBehavior{TRequest,TResponse}"/> decides whether every write in
/// the repo is committed or rolled back, and nothing unit-tested it before this project existed.
///
/// <para>
/// The first test here pins the repo's single most dangerous trap: the behavior rolls back only in
/// its <c>catch</c>, so a handler that writes and then returns <c>Result.Failure(...)</c> is
/// <b>committed</b>. That collides head-on with the repo-wide convention of "return a Result,
/// don't throw", which is why it catches people. Pinning it means a future refactor that silently
/// changes it — in either direction — has to confront the decision.
/// </para>
/// </summary>
[TestFixture]
public class TransactionBehaviorTests
{
    private Mock<IUnitOfWork> _unitOfWork = null!;

    private sealed record PlainCommand : IRequest<Result<string>>;

    private sealed record TransactionalCommand : IRequest<Result<string>>, ITransactionalCommand;

    [SetUp]
    public void SetUp()
    {
        _unitOfWork = new Mock<IUnitOfWork>();
        _unitOfWork.SetupGet(u => u.HasActiveTransaction).Returns(false);
    }

    private TransactionBehavior<TRequest, Result<string>> BehaviorFor<TRequest>()
        where TRequest : IRequest<Result<string>>
        => new(_unitOfWork.Object, NullLogger<TransactionBehavior<TRequest, Result<string>>>.Instance);

    /// <summary>
    /// The trap. A handler that returns a failure Result — the repo's normal way of reporting a
    /// business failure — still commits, because only an exception reaches the rollback path.
    /// A failure path that must not persist has to either write nothing or throw.
    /// </summary>
    [Test]
    public async Task Commits_EvenWhenTheHandlerReturnsAFailureResult()
    {
        var behavior = BehaviorFor<TransactionalCommand>();

        var response = await behavior.Handle(
            new TransactionalCommand(),
            _ => Task.FromResult(Result<string>.Failure(new Error("Some.Failure", "business rule rejected it"))),
            CancellationToken.None);

        Assert.That(response.IsSuccess, Is.False);
        _unitOfWork.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once,
            "a Result failure is not an exception, so the behavior commits — this is the documented trap");
        _unitOfWork.Verify(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Commits_WhenTheHandlerSucceeds()
    {
        var behavior = BehaviorFor<TransactionalCommand>();

        await behavior.Handle(
            new TransactionalCommand(),
            _ => Task.FromResult(Result<string>.Success("ok")),
            CancellationToken.None);

        _unitOfWork.Verify(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// The other half of the trap: throwing <i>is</i> what rolls back, and the exception must keep
    /// propagating so <c>ProblemDetailsExceptionMiddleware</c> can map it.
    /// </summary>
    [Test]
    public async Task RollsBackAndRethrows_WhenTheHandlerThrows()
    {
        var behavior = BehaviorFor<TransactionalCommand>();

        Assert.That(
            async () => await behavior.Handle(
                new TransactionalCommand(),
                _ => throw new InvalidOperationException("handler blew up"),
                CancellationToken.None),
            Throws.InstanceOf<InvalidOperationException>().With.Message.EqualTo("handler blew up"));

        _unitOfWork.Verify(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task DoesNothing_ForACommandThatIsNotMarkedTransactional()
    {
        var behavior = BehaviorFor<PlainCommand>();

        await behavior.Handle(
            new PlainCommand(),
            _ => Task.FromResult(Result<string>.Success("ok")),
            CancellationToken.None);

        _unitOfWork.Verify(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// The nested-transaction guard. An upstream flow (the idempotent consumer, for one) may
    /// already own a transaction; this behavior must not open or commit a second one, or it would
    /// commit work its caller still intended to be able to roll back.
    /// </summary>
    [Test]
    public async Task NoOps_WhenATransactionIsAlreadyActive()
    {
        _unitOfWork.SetupGet(u => u.HasActiveTransaction).Returns(true);
        var behavior = BehaviorFor<TransactionalCommand>();

        await behavior.Handle(
            new TransactionalCommand(),
            _ => Task.FromResult(Result<string>.Success("ok")),
            CancellationToken.None);

        _unitOfWork.Verify(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
