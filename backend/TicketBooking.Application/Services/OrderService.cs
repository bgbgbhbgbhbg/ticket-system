using TicketBooking.Application.Exceptions;
using TicketBooking.Application.Interfaces;
using TicketBooking.Application.Interfaces.Repositories;
using TicketBooking.Application.Interfaces.Services;
using TicketBooking.Domain.Entities;

namespace TicketBooking.Application.Services;

public class OrderService : IOrderService
{
    private const int MaxQuantityPerOrder = 10;

    private readonly IOrderRepository _orderRepository;
    private readonly ITicketRepository _ticketRepository;
    private readonly IMessagePublisher _messagePublisher;

    public OrderService(
        IOrderRepository orderRepository,
        ITicketRepository ticketRepository,
        IMessagePublisher messagePublisher)
    {
        _orderRepository = orderRepository;
        _ticketRepository = ticketRepository;
        _messagePublisher = messagePublisher;
    }

    public async Task<(Order Order, bool IsNew)> CreateOrderAsync(
        Guid userId,
        Guid ticketId,
        int quantity,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        // 1. 數量上限檢查（同步驗證，不需要查 DB）
        if (quantity > MaxQuantityPerOrder)
        {
            throw new OrderQuantityExceedsLimitException(quantity, MaxQuantityPerOrder);
        }

        // 2. Idempotency-Key 檢查：已存在就直接回傳原本的訂單（冪等語意，不是錯誤）
        var existingOrder = await _orderRepository.GetByIdempotencyKeyAsync(idempotencyKey, cancellationToken);
        if (existingOrder is not null)
        {
            return (existingOrder, false);
        }

        // 3. 確認票券存在（查不到就拋例外，對應 TICKET_NOT_FOUND 404）
        var ticket = await _ticketRepository.GetByIdAsync(ticketId, cancellationToken);
        if (ticket is null)
        {
            throw new TicketNotFoundException(ticketId);
        }

        // 4. 計算 TotalAmount 快照（quantity × 下單當下的票價，避免之後票價異動影響已建立訂單）
        var totalAmount = quantity * ticket.Price;

        // 5. 用 Order.Create() factory method 建立訂單（不指定 Id，讓 DB 的 uuidv7() 生效）
        var order = Order.Create(userId, ticketId, quantity, totalAmount, idempotencyKey);

        // 6. 持久化到資料庫
        var created = await _orderRepository.CreateAsync(order, cancellationToken);

        // 7. 發布 order.created 訊息到 RabbitMQ（Worker 消費後才執行庫存扣減，見 message-contracts.md）
        await _messagePublisher.PublishOrderCreatedAsync(
            created.Id,
            userId,
            ticketId,
            quantity,
            cancellationToken);

        return (created, true);
    }

    public async Task<Order?> GetOrderByIdAsync(
        Guid orderId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);

        // 查不到、或訂單不屬於目前使用者，一律回傳 null（對應 ORDER_NOT_FOUND 404）
        if (order is null || order.UserId != userId)
        {
            return null;
        }

        return order;
    }
}
