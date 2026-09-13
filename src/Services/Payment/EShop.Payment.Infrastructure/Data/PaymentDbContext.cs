using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Infrastructure.Data;
using EShop.Payment.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EShop.Payment.Infrastructure.Data;

public class PaymentDbContext : BaseDbContext
{
    public DbSet<PaymentTransaction> PaymentTransactions => Set<PaymentTransaction>();
    public DbSet<PaymentCustomer> PaymentCustomers => Set<PaymentCustomer>();
    public DbSet<ProcessedStripeWebhookEvent> ProcessedStripeWebhookEvents => Set<ProcessedStripeWebhookEvent>();

    public PaymentDbContext(DbContextOptions<PaymentDbContext> options)
        : base(options)
    {
    }

    public PaymentDbContext(DbContextOptions<PaymentDbContext> options, IMediator mediator)
        : base(options, mediator)
    {
    }

    public PaymentDbContext(
        DbContextOptions<PaymentDbContext> options,
        IMediator mediator,
        ICurrentUserContext currentUserContext)
        : base(options, mediator, currentUserContext)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PaymentTransaction>(entity =>
        {
            entity.ToTable("PaymentTransactions");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.OrderId)
                .IsRequired();

            entity.Property(x => x.UserId)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(x => x.StripeCustomerId)
                .HasMaxLength(200);

            entity.Property(x => x.Amount)
                .HasColumnType("decimal(18,2)")
                .IsRequired();

            entity.Property(x => x.Currency)
                .HasMaxLength(3)
                .IsRequired();

            entity.Property(x => x.PaymentMethod)
                .HasConversion<string>()
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(x => x.PaymentIntentId)
                .HasMaxLength(200);

            entity.Property(x => x.StripeStatus)
                .HasMaxLength(100);

            entity.Property(x => x.Status)
                .HasConversion<string>()
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(x => x.ErrorMessage)
                .HasMaxLength(500);

            entity.Property(x => x.CreatedAt)
                .HasColumnType("timestamp with time zone")
                .IsRequired();

            entity.Property(x => x.ProcessedAt)
                .HasColumnType("timestamp with time zone");

            entity.Property(x => x.UpdatedAt)
                .HasColumnType("timestamp with time zone");

            entity.Property(x => x.Version)
                .IsRowVersion();

            entity.HasIndex(x => x.OrderId).IsUnique();
            // Payment audit Stage 10 (M11). Unique among payments that have an intent. The webhook finds its payment by
            // intent id, and with two rows sharing one it would update whichever came first. Payments with no intent yet
            // hold '', so they are left out of the index.
            entity.HasIndex(x => x.PaymentIntentId)
                .IsUnique()
                .HasFilter("\"PaymentIntentId\" <> ''");
            entity.HasIndex(x => new { x.UserId, x.CreatedAt });
            entity.HasIndex(x => new { x.Status, x.CreatedAt });
        });

        modelBuilder.Entity<PaymentCustomer>(entity =>
        {
            entity.ToTable("PaymentCustomers");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.UserId)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(x => x.StripeCustomerId)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(x => x.CreatedAt)
                .HasColumnType("timestamp with time zone")
                .IsRequired();

            entity.Property(x => x.UpdatedAt)
                .HasColumnType("timestamp with time zone");

            entity.HasIndex(x => x.UserId).IsUnique();
            entity.HasIndex(x => x.StripeCustomerId).IsUnique();
        });

        modelBuilder.Entity<ProcessedStripeWebhookEvent>(entity =>
        {
            entity.ToTable("ProcessedStripeWebhookEvents");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.EventId)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(x => x.EventType)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(x => x.ProcessedAt)
                .HasColumnType("timestamp with time zone")
                .IsRequired();

            entity.HasIndex(x => x.EventId).IsUnique();
            entity.HasIndex(x => x.ProcessedAt);
        });

        base.OnModelCreating(modelBuilder);
    }
}
