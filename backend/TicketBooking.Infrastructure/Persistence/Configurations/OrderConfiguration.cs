using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TicketBooking.Domain.Entities;
using TicketBooking.Domain.Enums;

namespace TicketBooking.Infrastructure.Persistence.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders", t =>
        {
            t.HasCheckConstraint("ck_orders_quantity", "quantity > 0");
        });

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("uuidv7()");

        builder.Property(o => o.UserId)
            .HasColumnName("user_id")
            .IsRequired();
        builder.HasIndex(o => o.UserId)
            .HasDatabaseName("idx_orders_user_id");

        // 新增:外鍵約束(之前漏掉的部分),Restrict 避免刪除 User 時連帶砍掉訂單財務紀錄
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(o => o.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(o => o.TicketId)
            .HasColumnName("ticket_id")
            .IsRequired();
        builder.HasIndex(o => o.TicketId)
            .HasDatabaseName("idx_orders_ticket_id");

        // 新增:外鍵約束
        builder.HasOne<Ticket>()
            .WithMany()
            .HasForeignKey(o => o.TicketId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(o => o.Quantity)
            .HasColumnName("quantity")
            .IsRequired();

        builder.Property(o => o.TotalAmount)
            .HasColumnName("total_amount")
            .HasColumnType("numeric(10,2)")
            .IsRequired();

        // 修改:Status 現在是 OrderStatus enum,加 HasConversion<string> 讓資料庫存文字而不是整數,
        // 方便你直接在 DBeaver 裡看資料時一眼認出狀態,不用對照數字
        builder.Property(o => o.Status)
            .HasColumnName("status")
            .HasColumnType("varchar(20)")
            .HasConversion<string>()
            .IsRequired()
            .HasDefaultValue(OrderStatus.Pending);

        builder.Property(o => o.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .HasColumnType("varchar(100)")
            .IsRequired();
        // 複合 unique 索引：同一使用者不能重複相同 key，不同使用者可以有相同 UUID（防止跨用戶授權繞過）
        builder.HasIndex(o => new { o.UserId, o.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("idx_orders_user_idempotency_key");

        builder.Property(o => o.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired()
            .HasDefaultValueSql("now()");

        builder.Property(o => o.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamptz")
            .IsRequired()
            .HasDefaultValueSql("now()");
    }
}