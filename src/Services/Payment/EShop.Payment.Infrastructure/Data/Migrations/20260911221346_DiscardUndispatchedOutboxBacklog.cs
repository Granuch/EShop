using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EShop.Payment.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Ordering audit Stage 10. Until then Payment never registered <c>OutboxProcessorService</c>, so every
    /// integration event it wrote to <c>outbox_messages</c> is still pending. This closes that backlog
    /// <b>without sending it</b>, before the processor that now exists can start.
    ///
    /// <para>
    /// Why not deliver it late: the rows are stale observations, possibly months old. Sent now they would
    /// mark long-abandoned orders Paid or cancel them, and email customers about payments and refunds
    /// from long ago. Rows written after this migration — including by an old instance still running
    /// during a rolling update — are fresh and are dispatched normally.
    /// </para>
    /// <para>
    /// Why <c>Processed</c> and not <c>DeadLettered</c>: <c>OutboxHealthCheck</c> reports Unhealthy above
    /// ten dead-lettered rows, which would take readiness down for a deliberate discard. The rows keep
    /// their payload and carry <see cref="Marker"/> in <c>LastError</c>, so they stay identifiable until
    /// <c>OutboxCleanupService</c> deletes them with the rest of the processed rows (seven days).
    /// </para>
    /// <para>
    /// Ordering: Program.cs applies migrations after the host is built but before it runs, and hosted
    /// services start only when it runs, so no backlog row can be dispatched first.
    /// </para>
    /// </summary>
    public partial class DiscardUndispatchedOutboxBacklog : Migration
    {
        internal const string Marker =
            "Discarded without dispatch by migration DiscardUndispatchedOutboxBacklog: "
            + "Payment had no outbox processor before Ordering audit Stage 10.";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Status 1 = OutboxMessageStatus.Processed, 0 = Pending.
            migrationBuilder.Sql($"""
                UPDATE outbox_messages
                SET "Status" = 1, "ProcessedOnUtc" = now(), "LastError" = '{Marker}'
                WHERE "ProcessedOnUtc" IS NULL AND "Status" = 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Returns exactly the rows Up discarded, if cleanup has not deleted them yet. Only for rolling
            // back to code without the processor, where they would sit undispatched again.
            migrationBuilder.Sql($"""
                UPDATE outbox_messages
                SET "Status" = 0, "ProcessedOnUtc" = NULL, "LastError" = NULL
                WHERE "Status" = 1 AND "LastError" = '{Marker}';
                """);
        }
    }
}
