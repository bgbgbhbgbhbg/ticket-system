using TicketBooking.Domain.Enums;

namespace TicketBooking.Domain.Entities;

public class OrderStatusLog
{
    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public OrderStatus? FromStatus { get; private set; }
    public OrderStatus ToStatus { get; private set; }
    public string? Reason { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private OrderStatusLog() { }

    /// <summary>
    /// 每次 Order.TransitionTo() 成功後,由 Application 層(OrderService)呼叫這個 factory method
    /// 建立對應的 log,跟訂單狀態轉換寫在同一個 DB transaction 裡。
    /// </summary>
    public static OrderStatusLog Create(Guid orderId, OrderStatus? fromStatus, OrderStatus toStatus, string? reason)
    {
        return new OrderStatusLog
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            FromStatus = fromStatus,
            ToStatus = toStatus,
            Reason = reason,
            CreatedAt = DateTime.UtcNow
        };
    }
}