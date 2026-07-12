using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketBooking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixIdempotencyCompositeIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_orders_idempotency_key",
                table: "orders");

            migrationBuilder.CreateIndex(
                name: "idx_orders_user_idempotency_key",
                table: "orders",
                columns: new[] { "user_id", "idempotency_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_orders_user_idempotency_key",
                table: "orders");

            migrationBuilder.CreateIndex(
                name: "idx_orders_idempotency_key",
                table: "orders",
                column: "idempotency_key",
                unique: true);
        }
    }
}
