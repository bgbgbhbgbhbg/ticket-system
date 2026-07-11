using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TicketBooking.Domain.Entities;

namespace TicketBooking.Infrastructure.Persistence.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders");

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("uuidv7()");

        builder.Property(o => o.UserId)
            .HasColumnName("user_id")
            .IsRequired();
        builder.HasIndex(o => o.UserId)
            .HasDatabaseName("idx_orders_user_id");

        builder.Property(o => o.TicketId)
            .HasColumnName("ticket_id")
            .IsRequired();
        builder.HasIndex(o => o.TicketId)
            .HasDatabaseName("idx_orders_ticket_id");

        builder.Property(o => o.Quantity)
            .HasColumnName("quantity")
            .IsRequired();
        builder.HasCheckConstraint("ck_orders_quantity", "quantity > 0");

        builder.Property(o => o.TotalAmount)
            .HasColumnName("total_amount")
            .HasColumnType("numeric(10,2)")
            .IsRequired();

        builder.Property(o => o.Status)
            .HasColumnName("status")
            .HasColumnType("varchar(20)")
            .IsRequired()
            .HasDefaultValue("Pending");

        builder.Property(o => o.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .HasColumnType("varchar(100)")
            .IsRequired();
        builder.HasIndex(o => o.IdempotencyKey)
            .IsUnique()
            .HasDatabaseName("idx_orders_idempotency_key");

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

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(o => o.UserId)
            .OnDelete(DeleteBehavior.Restrict);   // 不允許刪除 User 時連帶砍掉他的訂單紀錄

        builder.HasOne<Ticket>()
            .WithMany()
            .HasForeignKey(o => o.TicketId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
