using EShop.BuildingBlocks.Infrastructure.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EShop.BuildingBlocks.Infrastructure.Data.Configurations;

/// <summary>
/// EF Core mapping for <see cref="AuditLogEntry"/> (migration M11). Applied by both base contexts, so every service
/// with a database carries the table; each service's own migration creates it.
/// </summary>
public class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> builder)
    {
        builder.ToTable("audit_log");

        builder.HasKey(x => x.Id);

        // Database-generated and monotonic: it is the paging cursor (see AuditLogEntry).
        builder.Property(x => x.Id).ValueGeneratedOnAdd();

        builder.Property(x => x.OccurredAt).IsRequired();
        builder.Property(x => x.Service).HasMaxLength(AuditLogEntry.ServiceMaxLength).IsRequired();
        builder.Property(x => x.Action).HasMaxLength(AuditLogEntry.ActionMaxLength).IsRequired();
        builder.Property(x => x.EntityType).HasMaxLength(AuditLogEntry.EntityTypeMaxLength).IsRequired();
        builder.Property(x => x.EntityId).HasMaxLength(AuditLogEntry.EntityIdMaxLength);
        builder.Property(x => x.ActorUserId).HasMaxLength(AuditLogEntry.ActorUserIdMaxLength);
        builder.Property(x => x.ActorName).HasMaxLength(AuditLogEntry.ActorNameMaxLength);
        builder.Property(x => x.CorrelationId).HasMaxLength(AuditLogEntry.CorrelationIdMaxLength);
        builder.Property(x => x.Outcome).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.ErrorCode).HasMaxLength(AuditLogEntry.ErrorCodeMaxLength);
        builder.Property(x => x.PayloadJson);

        // The two questions an operator asks: "what happened to this thing?" and "what did this person do?". Paging is
        // by Id, which the primary key already serves in either direction.
        builder.HasIndex(x => new { x.EntityType, x.EntityId })
            .HasDatabaseName("IX_AuditLog_EntityType_EntityId");

        builder.HasIndex(x => x.ActorUserId)
            .HasDatabaseName("IX_AuditLog_ActorUserId");
    }
}
