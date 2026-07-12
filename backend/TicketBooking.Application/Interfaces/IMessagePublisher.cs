namespace TicketBooking.Application.Interfaces;

/// <summary>
/// 訊息發布 Interface（實作放在 Infrastructure/Messaging/，遵循 Clean Architecture）
/// </summary>
public interface IMessagePublisher
{
    /// <summary>
    /// 發布 order.created 事件到 RabbitMQ（對應 docs/3_specs/message-contracts.md 第 2.1 節）
    /// </summary>
    Task PublishOrderCreatedAsync(
        Guid orderId,
        Guid userId,
        Guid ticketId,
        int quantity,
        CancellationToken cancellationToken = default);
}
