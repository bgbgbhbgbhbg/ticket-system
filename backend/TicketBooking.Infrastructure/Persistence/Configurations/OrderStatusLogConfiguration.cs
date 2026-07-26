using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TicketBooking.Domain.Entities;

namespace TicketBooking.Infrastructure.Persistence.Configurations;

public class OrderStatusLogConfiguration : IEntityTypeConfiguration<OrderStatusLog>
{
    public void Configure(EntityTypeBuilder<OrderStatusLog> builder)
    {
        builder.ToTable("order_status_logs");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("uuidv7()");

        builder.Property(l => l.OrderId)
            .HasColumnName("order_id")
            .IsRequired();

        // 新增:外鍵約束(之前漏掉的部分)
        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(l => l.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        // EF Core 自動產生的外鍵索引改為統一的 idx_xxx 命名慣例（tech-debt #2）
        builder.HasIndex(l => l.OrderId)
            .HasDatabaseName("idx_order_status_logs_order_id");

        // 修改:FromStatus 是 OrderStatus?(nullable enum,對應初始狀態為 NULL),加 HasConversion<string>
        builder.Property(l => l.FromStatus)
            .HasColumnName("from_status")
            .HasColumnType("varchar(20)")
            .HasConversion<string>();

        // 修改:ToStatus 是 OrderStatus enum,加 HasConversion<string>
        builder.Property(l => l.ToStatus)
            .HasColumnName("to_status")
            .HasColumnType("varchar(20)")
            .HasConversion<string>()
            .IsRequired();

        builder.Property(l => l.Reason)
            .HasColumnName("reason")
            .HasColumnType("varchar(500)");

        builder.Property(l => l.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired()
            .HasDefaultValueSql("now()");
    }
}