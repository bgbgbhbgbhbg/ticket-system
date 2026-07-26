using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketBooking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameOrderStatusLogIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "IX_order_status_logs_order_id",
                table: "order_status_logs",
                newName: "idx_order_status_logs_order_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "idx_order_status_logs_order_id",
                table: "order_status_logs",
                newName: "IX_order_status_logs_order_id");
        }
    }
}
