using EShop.BuildingBlocks.Domain.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EShop.BuildingBlocks.Infrastructure.Data.Configurations;

/// <summary>
/// EF Core configuration for the OutboxMessage entity.
/// Optimized for efficient polling and processing.
/// </summary>
public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");

        builder.HasKey(x => x.Id);

        // Id is the EventId from the domain event - used for idempotency
        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.Type)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(x => x.Payload)
            .IsRequired();

        builder.Property(x => x.OccurredOnUtc)
            .IsRequired();

        builder.Property(x => x.ProcessedOnUtc);

        builder.Property(x => x.RetryCount)
            .HasDefaultValue(0);

        builder.Property(x => x.LastError)
            .HasMaxLength(4000);

        builder.Property(x => x.CorrelationId)
            .HasMaxLength(100);

        builder.Property(x => x.AggregateType)
            .HasMaxLength(250);

        builder.Property(x => x.AggregateId)
            .HasMaxLength(100);

        builder.Property(x => x.Status)
            .HasDefaultValue(OutboxMessageStatus.Pending)
            .IsRequired();

        // Index for efficient polling of unprocessed messages
        // Covers: WHERE "Status" = 0 ORDER BY OccurredOnUtc
        builder.HasIndex(x => new { x.Status, x.OccurredOnUtc })
            .HasDatabaseName("IX_OutboxMessages_Status_OccurredOnUtc");

        // Index for efficient polling of unprocessed messages (legacy — kept for backward compat with raw SQL)
        // Covers: WHERE ProcessedOnUtc IS NULL ORDER BY OccurredOnUtc
        builder.HasIndex(x => new { x.ProcessedOnUtc, x.OccurredOnUtc })
            .HasDatabaseName("IX_OutboxMessages_ProcessedOnUtc_OccurredOnUtc");

        // Index for finding failed messages that can be retried
        builder.HasIndex(x => new { x.ProcessedOnUtc, x.RetryCount })
            .HasDatabaseName("IX_OutboxMessages_ProcessedOnUtc_RetryCount")
            .HasFilter("\"ProcessedOnUtc\" IS NULL");

        // Index for correlation ID lookups (distributed tracing)
        builder.HasIndex(x => x.CorrelationId)
            .HasDatabaseName("IX_OutboxMessages_CorrelationId")
            .HasFilter("\"CorrelationId\" IS NOT NULL");
    }
}
