namespace TicketBooking.Domain.Entities;

public class Ticket
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string EventName { get; set; } = null!;
    public DateTime EventStartAt { get; set; }
    public int TotalQuantity { get; set; }
    public int AvailableQuantity { get; set; }
    public decimal Price { get; set; }
    public int Version { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
