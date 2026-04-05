using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Infrastructure.Data;
using EShop.Notification.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EShop.Notification.Infrastructure.Data;

public sealed class NotificationDbContext : BaseDbContext
{
    public DbSet<NotificationLog> NotificationLogs => Set<NotificationLog>();

    public NotificationDbContext(DbContextOptions<NotificationDbContext> options)
        : base(options)
    {
    }

    public NotificationDbContext(DbContextOptions<NotificationDbContext> options, IMediator mediator)
        : base(options, mediator)
    {
    }

    public NotificationDbContext(
        DbContextOptions<NotificationDbContext> options,
        IMediator mediator,
        ICurrentUserContext currentUserContext)
        : base(options, mediator, currentUserContext)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NotificationLog>(entity =>
        {
            entity.ToTable("NotificationLogs");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.EventId)
                .IsRequired();

            entity.Property(x => x.EventType)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(x => x.CorrelationId)
                .HasMaxLength(100);

            entity.Property(x => x.UserId)
                .HasMaxLength(100);

            entity.Property(x => x.RecipientEmail)
                .HasMaxLength(320)
                .IsRequired();

            entity.Property(x => x.TemplateName)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(x => x.Subject)
                .HasMaxLength(300)
                .IsRequired();

            entity.Property(x => x.Status)
                .HasConversion<int>()
                .IsRequired();

            entity.Property(x => x.RetryCount)
                .HasDefaultValue(0)
                .IsRequired();

            entity.Property(x => x.LastError)
                .HasMaxLength(4000);

            entity.Property(x => x.ProviderMessageId)
                .HasMaxLength(200);

            entity.Property(x => x.CreatedAt)
                .HasColumnType("timestamp with time zone")
                .IsRequired();

            entity.Property(x => x.SentAt)
                .HasColumnType("timestamp with time zone");

            entity.Property(x => x.UpdatedAt)
                .HasColumnType("timestamp with time zone");

            entity.HasIndex(x => x.EventId).IsUnique();
            entity.HasIndex(x => new { x.Status, x.CreatedAt });
            entity.HasIndex(x => x.CorrelationId);
            entity.HasIndex(x => x.UserId);
        });

        base.OnModelCreating(modelBuilder);
    }
}
