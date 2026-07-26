namespace TicketBooking.Application.Exceptions;

public class OrderNotFoundException : Exception
{
    public Guid OrderId { get; }
    public OrderNotFoundException(Guid orderId)
        : base($"Order '{orderId}' not found.")
    {
        OrderId = orderId;
    }
}
