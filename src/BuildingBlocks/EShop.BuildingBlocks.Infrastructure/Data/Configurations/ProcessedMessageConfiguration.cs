using EShop.BuildingBlocks.Infrastructure.Consumers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EShop.BuildingBlocks.Infrastructure.Data.Configurations;

/// <summary>
/// EF Core configuration for the ProcessedMessage entity.
/// Used for consumer-side idempotency tracking.
/// </summary>
public class ProcessedMessageConfiguration : IEntityTypeConfiguration<ProcessedMessage>
{
    public void Configure(EntityTypeBuilder<ProcessedMessage> builder)
    {
        builder.ToTable("processed_messages");

        builder.HasKey(x => x.MessageId);

        builder.Property(x => x.MessageId)
            .ValueGeneratedNever();

        builder.Property(x => x.MessageType)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(x => x.ProcessedOnUtc)
            .IsRequired();

        // Index for cleanup of old records
        builder.HasIndex(x => x.ProcessedOnUtc)
            .HasDatabaseName("IX_ProcessedMessages_ProcessedOnUtc");
    }
}
