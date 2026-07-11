namespace TicketBooking.Domain.Entities;

public class Ticket
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public string EventName { get; private set; } = null!;
    public DateTime EventStartAt { get; private set; }
    public int TotalQuantity { get; private set; }
    public int AvailableQuantity { get; private set; }
    public decimal Price { get; private set; }
    public int Version { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private Ticket() { }

    public static Ticket Create(string name, string eventName, DateTime eventStartAt, int totalQuantity, decimal price)
    {
        var now = DateTime.UtcNow;
        return new Ticket
        {
            Id = Guid.NewGuid(),
            Name = name,
            EventName = eventName,
            EventStartAt = eventStartAt,
            TotalQuantity = totalQuantity,
            AvailableQuantity = totalQuantity,   // 強制等於 TotalQuantity,不讓外部指定不同的值
            Price = price,
            Version = 0,                         // 強制從 0 開始,不讓外部指定
            CreatedAt = now,
            UpdatedAt = now
        };
    }
}