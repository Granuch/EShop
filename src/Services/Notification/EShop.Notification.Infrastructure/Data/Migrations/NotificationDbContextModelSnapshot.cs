using System;
using EShop.Notification.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace EShop.Notification.Infrastructure.Data.Migrations
{
    [DbContext(typeof(NotificationDbContext))]
    partial class NotificationDbContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
            modelBuilder
                .HasAnnotation("ProductVersion", "10.0.2");

            modelBuilder.Entity("EShop.BuildingBlocks.Domain.Outbox.OutboxMessage", b =>
            {
                b.Property<Guid>("Id")
                    .ValueGeneratedNever()
                    .HasColumnType("uuid");

                b.Property<string>("AggregateId")
                    .HasMaxLength(100)
                    .HasColumnType("character varying(100)");

                b.Property<string>("AggregateType")
                    .HasMaxLength(250)
                    .HasColumnType("character varying(250)");

                b.Property<string>("CorrelationId")
                    .HasMaxLength(100)
                    .HasColumnType("character varying(100)");

                b.Property<DateTime>("OccurredOnUtc")
                    .HasColumnType("timestamp with time zone");

                b.Property<string>("Payload")
                    .IsRequired()
                    .HasColumnType("text");

                b.Property<DateTime?>("ProcessedOnUtc")
                    .HasColumnType("timestamp with time zone");

                b.Property<int>("RetryCount")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("integer")
                    .HasDefaultValue(0);

                b.Property<string>("LastError")
                    .HasMaxLength(4000)
                    .HasColumnType("character varying(4000)");

                b.Property<int>("Status")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("integer")
                    .HasDefaultValue(0);

                b.Property<string>("Type")
                    .IsRequired()
                    .HasMaxLength(500)
                    .HasColumnType("character varying(500)");

                b.HasKey("Id");

                b.HasIndex("CorrelationId")
                    .HasDatabaseName("IX_OutboxMessages_CorrelationId")
                    .HasFilter("\"CorrelationId\" IS NOT NULL");

                b.HasIndex("ProcessedOnUtc", "OccurredOnUtc")
                    .HasDatabaseName("IX_OutboxMessages_ProcessedOnUtc_OccurredOnUtc");

                b.HasIndex("ProcessedOnUtc", "RetryCount")
                    .HasDatabaseName("IX_OutboxMessages_ProcessedOnUtc_RetryCount")
                    .HasFilter("\"ProcessedOnUtc\" IS NULL");

                b.HasIndex("Status", "OccurredOnUtc")
                    .HasDatabaseName("IX_OutboxMessages_Status_OccurredOnUtc");

                b.ToTable("outbox_messages");
            });

            modelBuilder.Entity("EShop.BuildingBlocks.Infrastructure.Consumers.ProcessedMessage", b =>
            {
                b.Property<Guid>("MessageId")
                    .ValueGeneratedNever()
                    .HasColumnType("uuid");

                b.Property<string>("MessageType")
                    .IsRequired()
                    .HasMaxLength(500)
                    .HasColumnType("character varying(500)");

                b.Property<DateTime>("ProcessedOnUtc")
                    .HasColumnType("timestamp with time zone");

                b.HasKey("MessageId");

                b.HasIndex("ProcessedOnUtc")
                    .HasDatabaseName("IX_ProcessedMessages_ProcessedOnUtc");

                b.ToTable("processed_messages");
            });

            modelBuilder.Entity("EShop.Notification.Domain.Entities.NotificationLog", b =>
            {
                b.Property<Guid>("Id")
                    .ValueGeneratedNever()
                    .HasColumnType("uuid");

                b.Property<string>("CorrelationId")
                    .HasMaxLength(100)
                    .HasColumnType("character varying(100)");

                b.Property<DateTime>("CreatedAt")
                    .HasColumnType("timestamp with time zone");

                b.Property<Guid>("EventId")
                    .HasColumnType("uuid");

                b.Property<string>("EventType")
                    .IsRequired()
                    .HasMaxLength(200)
                    .HasColumnType("character varying(200)");

                b.Property<string>("LastError")
                    .HasMaxLength(4000)
                    .HasColumnType("character varying(4000)");

                b.Property<string>("ProviderMessageId")
                    .HasMaxLength(200)
                    .HasColumnType("character varying(200)");

                b.Property<string>("RecipientEmail")
                    .IsRequired()
                    .HasMaxLength(320)
                    .HasColumnType("character varying(320)");

                b.Property<int>("RetryCount")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("integer")
                    .HasDefaultValue(0);

                b.Property<DateTime?>("SentAt")
                    .HasColumnType("timestamp with time zone");

                b.Property<int>("Status")
                    .HasColumnType("integer");

                b.Property<string>("Subject")
                    .IsRequired()
                    .HasMaxLength(300)
                    .HasColumnType("character varying(300)");

                b.Property<string>("TemplateName")
                    .IsRequired()
                    .HasMaxLength(100)
                    .HasColumnType("character varying(100)");

                b.Property<DateTime?>("UpdatedAt")
                    .HasColumnType("timestamp with time zone");

                b.Property<string>("UserId")
                    .HasMaxLength(100)
                    .HasColumnType("character varying(100)");

                b.HasKey("Id");

                b.HasIndex("CorrelationId");

                b.HasIndex("EventId")
                    .IsUnique();

                b.HasIndex("Status", "CreatedAt");

                b.HasIndex("UserId");

                b.ToTable("NotificationLogs");
            });
        }
    }
}
