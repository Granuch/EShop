using System.Text.Json;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Application.Auditing;
using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Infrastructure.Auditing;
using EShop.BuildingBlocks.Infrastructure.Data.Configurations;
using EShop.BuildingBlocks.Infrastructure.Extensions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace EShop.BuildingBlocks.UnitTests.Auditing;

/// <summary>
/// Admin panel S15. <see cref="AuditBehavior{TRequest,TResponse}"/> records one row per audited command, whatever the
/// outcome, through its own scope.
///
/// <para>
/// Every test resolves the behavior from the production registration (<c>AddEShopAuditLog</c>) and a real scope,
/// rather than constructing it — the own-scope property is a fact about DI, and a hand-built writer would be separate
/// from the caller's context by construction and could not show it being lost.
/// </para>
/// </summary>
[TestFixture]
public class AuditBehaviorTests
{
    private const string ActorId = "admin-1";

    private ServiceProvider _provider = null!;
    private string _databaseName = null!;

    [SetUp]
    public void SetUp()
    {
        _databaseName = Guid.NewGuid().ToString();
        _provider = BuildProvider(services => { });
    }

    [TearDown]
    public void TearDown() => _provider.Dispose();

    private ServiceProvider BuildProvider(Action<IServiceCollection> customize)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AuditTestDbContext>(o => o.UseInMemoryDatabase(_databaseName));

        var user = new Mock<ICurrentUserContext>();
        user.SetupGet(u => u.UserId).Returns(ActorId);
        user.SetupGet(u => u.UserName).Returns("admin@test.com");
        user.SetupGet(u => u.CorrelationId).Returns("corr-1");
        services.AddScoped(_ => user.Object);

        services.AddEShopAuditLog<AuditTestDbContext>("svc");
        customize(services);
        return services.BuildServiceProvider();
    }

    // ---------- the commands ----------

    private sealed record RenameWidgetCommand : IRequest<Result>, IAuditedCommand
    {
        public Guid WidgetId { get; init; } = Guid.Parse("11111111-1111-1111-1111-111111111111");
        public string Name { get; init; } = "Blue widget";
        public string Password { get; init; } = "hunter2-by-name";

        [SensitiveData]
        public string Contact { get; init; } = "hunter2-by-attribute";

        string IAuditedCommand.AuditEntityType => "Widget";
        string? IAuditedCommand.AuditEntityId => WidgetId.ToString();
    }

    private sealed record CreateWidgetCommand : IRequest<Result<Guid>>, IAuditedCommand
    {
        public string Name { get; init; } = "New widget";

        string IAuditedCommand.AuditEntityType => "Widget";
        string? IAuditedCommand.AuditEntityId => null;
        string? IAuditedCommand.AuditEntityIdFromResult(object? value) => value is Guid id ? id.ToString() : null;
    }

    private sealed record ReadWidgetQuery : IRequest<Result>;

    private async Task<TResponse> Send<TRequest, TResponse>(
        TRequest request,
        Func<IServiceProvider, Task<TResponse>> handler,
        IServiceProvider? provider = null)
        where TRequest : IRequest<TResponse>
    {
        using var scope = (provider ?? _provider).CreateScope();
        var behaviors = scope.ServiceProvider.GetServices<IPipelineBehavior<TRequest, TResponse>>().ToList();
        Assert.That(behaviors, Has.Count.EqualTo(1), "AddEShopAuditLog registers exactly one behavior");

        return await behaviors[0].Handle(request, _ => handler(scope.ServiceProvider), CancellationToken.None);
    }

    private List<AuditLogEntry> Rows()
    {
        using var scope = _provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AuditTestDbContext>().Set<AuditLogEntry>().AsNoTracking().ToList();
    }

    // ---------- outcomes ----------

    [Test]
    public async Task ASuccessfulCommand_WritesOneRow_NamingTheActorTheEntityAndTheAction()
    {
        await Send<RenameWidgetCommand, Result>(new RenameWidgetCommand(), _ => Task.FromResult(Result.Success()));

        var row = Rows().Single();
        Assert.Multiple(() =>
        {
            Assert.That(row.Service, Is.EqualTo("svc"));
            Assert.That(row.Action, Is.EqualTo("RenameWidget"), "the type name without its Command suffix");
            Assert.That(row.EntityType, Is.EqualTo("Widget"));
            Assert.That(row.EntityId, Is.EqualTo("11111111-1111-1111-1111-111111111111"));
            Assert.That(row.ActorUserId, Is.EqualTo(ActorId));
            Assert.That(row.ActorName, Is.EqualTo("admin@test.com"));
            Assert.That(row.CorrelationId, Is.EqualTo("corr-1"));
            Assert.That(row.Outcome, Is.EqualTo(AuditOutcome.Succeeded));
            Assert.That(row.ErrorCode, Is.Null);
            Assert.That(row.OccurredAt, Is.EqualTo(DateTime.UtcNow).Within(TimeSpan.FromMinutes(1)));
        });
    }

    [Test]
    public async Task ThePayload_IsTheRequestsPublicProperties_WithSecretsRedactedByNameAndByAttribute()
    {
        await Send<RenameWidgetCommand, Result>(new RenameWidgetCommand(), _ => Task.FromResult(Result.Success()));

        var payload = Rows().Single().PayloadJson!;
        using var json = JsonDocument.Parse(payload);
        var root = json.RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("name").GetString(), Is.EqualTo("Blue widget"));
            Assert.That(root.GetProperty("widgetId").GetString(), Is.EqualTo("11111111-1111-1111-1111-111111111111"));
            Assert.That(root.GetProperty("password").GetString(), Is.EqualTo("****"));
            Assert.That(root.GetProperty("contact").GetString(), Is.EqualTo("****"));

            // By value, not by field name: the secrets must be absent wherever they might have landed.
            Assert.That(payload, Does.Not.Contain("hunter2"));

            // The explicit IAuditedCommand members are not public properties, so they are not payload.
            Assert.That(root.TryGetProperty("auditEntityType", out _), Is.False);
        });
    }

    [Test]
    public async Task AResultFailure_IsRecordedAsRejected_WithItsErrorCode()
    {
        var response = await Send<RenameWidgetCommand, Result>(
            new RenameWidgetCommand(),
            _ => Task.FromResult(Result.Failure(new Error("Widget.NotFound", "No such widget"))));

        var row = Rows().Single();
        Assert.Multiple(() =>
        {
            Assert.That(response.IsFailure, Is.True, "the caller's response is untouched");
            Assert.That(row.Outcome, Is.EqualTo(AuditOutcome.Rejected));
            Assert.That(row.ErrorCode, Is.EqualTo("Widget.NotFound"));
            Assert.That(row.EntityId, Is.EqualTo("11111111-1111-1111-1111-111111111111"));
        });
    }

    [Test]
    public async Task AGenericResultFailure_IsRecordedAsRejected_WithNoEntityIdFromItsDefaultValue()
    {
        await Send<CreateWidgetCommand, Result<Guid>>(
            new CreateWidgetCommand(),
            _ => Task.FromResult(Result<Guid>.Failure(new Error("Widget.NameTaken", "Taken"))));

        var row = Rows().Single();
        Assert.Multiple(() =>
        {
            Assert.That(row.Outcome, Is.EqualTo(AuditOutcome.Rejected));
            Assert.That(row.ErrorCode, Is.EqualTo("Widget.NameTaken"));
            // A failed Result<Guid> carries default(Guid); reading an id from it would name a widget that never existed.
            Assert.That(row.EntityId, Is.Null);
        });
    }

    [Test]
    public async Task ACreate_TakesItsEntityIdFromTheResult()
    {
        var created = Guid.NewGuid();

        await Send<CreateWidgetCommand, Result<Guid>>(new CreateWidgetCommand(), _ => Task.FromResult(Result<Guid>.Success(created)));

        var row = Rows().Single();
        Assert.Multiple(() =>
        {
            Assert.That(row.Outcome, Is.EqualTo(AuditOutcome.Succeeded));
            Assert.That(row.EntityId, Is.EqualTo(created.ToString()));
        });
    }

    [Test]
    public void AThrowingCommand_IsRecordedAsFailed_WithTheExceptionTypeOnly_AndTheSameExceptionPropagates()
    {
        var thrown = new InvalidOperationException("duplicate key value violates unique constraint \"IX_secret_schema\"");

        var caught = Assert.ThrowsAsync<InvalidOperationException>(() =>
            Send<RenameWidgetCommand, Result>(new RenameWidgetCommand(), _ => throw thrown));

        var row = Rows().Single();
        Assert.Multiple(() =>
        {
            Assert.That(caught, Is.SameAs(thrown));
            Assert.That(row.Outcome, Is.EqualTo(AuditOutcome.Failed));
            Assert.That(row.ErrorCode, Is.EqualTo(nameof(InvalidOperationException)));
            Assert.That(row.PayloadJson, Does.Not.Contain("IX_secret_schema"), "the message never reaches the table");
        });
    }

    [Test]
    public async Task ARequestThatIsNotAudited_WritesNothing()
    {
        await Send<ReadWidgetQuery, Result>(new ReadWidgetQuery(), _ => Task.FromResult(Result.Success()));

        Assert.That(Rows(), Is.Empty);
    }

    // ---------- isolation ----------

    [Test]
    public void TheRowIsWrittenThroughItsOwnScope_SoTheCommandsUnsavedChangesAreNotSavedWithIt()
    {
        // The command adds an entity and then fails before saving — which is what a rolled-back command leaves in its
        // context. Written through that context, the audit row's save would persist the widget as well.
        Assert.ThrowsAsync<InvalidOperationException>(() =>
            Send<RenameWidgetCommand, Result>(new RenameWidgetCommand(), sp =>
            {
                sp.GetRequiredService<AuditTestDbContext>().Widgets.Add(new Widget { Id = Guid.NewGuid() });
                throw new InvalidOperationException("the command failed after tracking a change");
            }));

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditTestDbContext>();
        Assert.Multiple(() =>
        {
            Assert.That(db.Set<AuditLogEntry>().Count(), Is.EqualTo(1), "the failure is recorded");
            Assert.That(db.Widgets.Count(), Is.Zero, "and nothing the failed command tracked was saved with it");
        });
    }

    [Test]
    public async Task AFailedAuditWrite_DoesNotFailACommandThatAlreadySucceeded()
    {
        var writer = new Mock<IAuditLogWriter>();
        writer.Setup(w => w.WriteAsync(It.IsAny<AuditLogEntry>())).ThrowsAsync(new InvalidOperationException("database down"));

        using var provider = BuildProvider(services => services.AddSingleton(writer.Object));

        var response = await Send<RenameWidgetCommand, Result>(
            new RenameWidgetCommand(), _ => Task.FromResult(Result.Success()), provider);

        Assert.Multiple(() =>
        {
            Assert.That(response.IsSuccess, Is.True);
            writer.Verify(w => w.WriteAsync(It.IsAny<AuditLogEntry>()), Times.Once);
        });
    }

    [Test]
    public void AnOverlongCorrelationId_IsCutToTheColumn_RatherThanFailingTheInsert()
    {
        // X-Correlation-ID is client-supplied. Postgres would reject an uncut value with 22001 and the row would be lost.
        var entry = AuditLogEntry.Record(
            DateTime.UtcNow, "svc", "RenameWidget", "Widget", "1", ActorId, null, new string('c', 5000),
            AuditOutcome.Succeeded, new string('e', 5000), "{}");

        Assert.Multiple(() =>
        {
            Assert.That(entry.CorrelationId, Has.Length.EqualTo(AuditLogEntry.CorrelationIdMaxLength));
            Assert.That(entry.ErrorCode, Has.Length.EqualTo(AuditLogEntry.ErrorCodeMaxLength));
        });
    }

    // ---------- fixtures ----------

    public sealed class Widget
    {
        public Guid Id { get; set; }
    }

    private sealed class AuditTestDbContext(DbContextOptions<AuditTestDbContext> options) : DbContext(options)
    {
        public DbSet<Widget> Widgets => Set<Widget>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new AuditLogEntryConfiguration());
            modelBuilder.Entity<Widget>().HasKey(w => w.Id);
        }
    }
}
