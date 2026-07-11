using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TicketBooking.Domain.Entities;

namespace TicketBooking.Infrastructure.Persistence.Configurations;

public class TicketConfiguration : IEntityTypeConfiguration<Ticket>
{
    public void Configure(EntityTypeBuilder<Ticket> builder)
    {
        builder.ToTable("tickets", t =>
        {
            t.HasCheckConstraint("ck_tickets_total_quantity", "total_quantity >= 0");
            t.HasCheckConstraint("ck_tickets_available_quantity", "available_quantity >= 0");
            t.HasCheckConstraint("ck_tickets_price", "price >= 0");
        });

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("uuidv7()");

        builder.Property(t => t.Name)
            .HasColumnName("name")
            .HasColumnType("varchar(200)")
            .IsRequired();

        builder.Property(t => t.EventName)
            .HasColumnName("event_name")
            .HasColumnType("varchar(200)")
            .IsRequired();

        builder.Property(t => t.EventStartAt)
            .HasColumnName("event_start_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(t => t.TotalQuantity)
            .HasColumnName("total_quantity")
            .IsRequired();

        builder.Property(t => t.AvailableQuantity)
            .HasColumnName("available_quantity")
            .IsRequired();

        builder.Property(t => t.Price)
            .HasColumnName("price")
            .HasColumnType("numeric(10,2)")
            .IsRequired();

        builder.Property(t => t.Version)
            .HasColumnName("version")
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(t => t.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired()
            .HasDefaultValueSql("now()");

        builder.Property(t => t.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamptz")
            .IsRequired()
            .HasDefaultValueSql("now()");

        builder.HasIndex(t => t.EventStartAt)
            .HasDatabaseName("idx_tickets_event_start_at");
    }
}
