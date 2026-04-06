using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Infrastructure.Data;
using EShop.Payment.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EShop.Payment.Infrastructure.Data;

public class PaymentDbContext : BaseDbContext
{
    public DbSet<PaymentTransaction> PaymentTransactions => Set<PaymentTransaction>();

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

            entity.Property(x => x.Amount)
                .HasColumnType("decimal(18,2)")
                .IsRequired();

            entity.Property(x => x.Currency)
                .HasMaxLength(3)
                .IsRequired();

            entity.Property(x => x.PaymentMethod)
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(x => x.PaymentIntentId)
                .HasMaxLength(200);

            entity.Property(x => x.Status)
                .HasConversion<string>()
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(x => x.ErrorMessage)
                .HasMaxLength(500);

            entity.Property(x => x.RetryCount)
                .HasDefaultValue(0)
                .IsRequired();

            entity.Property(x => x.CreatedAt)
                .HasColumnType("timestamp with time zone")
                .IsRequired();

            entity.Property(x => x.ProcessedAt)
                .HasColumnType("timestamp with time zone");

            entity.Property(x => x.UpdatedAt)
                .HasColumnType("timestamp with time zone");

            entity.HasIndex(x => x.OrderId).IsUnique();
            entity.HasIndex(x => new { x.UserId, x.CreatedAt });
            entity.HasIndex(x => new { x.Status, x.CreatedAt });
        });

        base.OnModelCreating(modelBuilder);
    }
}
