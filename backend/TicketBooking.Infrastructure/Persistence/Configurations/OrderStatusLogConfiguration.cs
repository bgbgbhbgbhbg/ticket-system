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

        builder.Property(l => l.FromStatus)
            .HasColumnName("from_status")
            .HasColumnType("varchar(20)");

        builder.Property(l => l.ToStatus)
            .HasColumnName("to_status")
            .HasColumnType("varchar(20)")
            .IsRequired();

        builder.Property(l => l.Reason)
            .HasColumnName("reason")
            .HasColumnType("varchar(500)");

        builder.Property(l => l.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired()
            .HasDefaultValueSql("now()");

        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(l => l.OrderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
