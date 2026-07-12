using Microsoft.Extensions.Logging;
using TicketBooking.Application.Interfaces.Repositories;
using TicketBooking.Application.Interfaces.Services;
using TicketBooking.Domain.Entities;
using TicketBooking.Domain.Enums;

namespace TicketBooking.Application.Services;

/// <summary>
/// 訂單處理服務，實作樂觀鎖 CAS + bounded retry 邏輯。
/// 對應 docs/3_specs/domain-state-machine.md 第 3、4、5 節。
/// </summary>
public class OrderProcessingService : IOrderProcessingService
{
    /// <summary>
    /// 最大重試次數（version 衝突時），對應 domain-state-machine.md 第 4 節。
    /// 超過此次數 → Failed("optimistic_lock_retry_exhausted")。
    /// </summary>
    private const int MaxRetries = 3;

    private readonly IOrderRepository _orderRepository;
    private readonly ITicketRepository _ticketRepository;
    private readonly ILogger<OrderProcessingService> _logger;

    public OrderProcessingService(
        IOrderRepository orderRepository,
        ITicketRepository ticketRepository,
        ILogger<OrderProcessingService> logger)
    {
        _orderRepository = orderRepository;
        _ticketRepository = ticketRepository;
        _logger = logger;
    }

    public async Task ProcessOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        // ── 1. 讀取訂單 ──────────────────────────────────────────────────────
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            _logger.LogWarning("Order {OrderId} 不存在，跳過處理", orderId);
            return; // 業務上的異常（訊息裡的 orderId 找不到），ack 掉避免無限重試
        }

        // ── 2. Pending → Processing ─────────────────────────────────────────
        var fromStatus = order.Status;
        order.TransitionTo(OrderStatus.Processing, "worker_picked_up");
        var processingLog = OrderStatusLog.Create(
            order.Id, fromStatus, OrderStatus.Processing, "worker_picked_up");
        await _orderRepository.UpdateAndAddStatusLogAsync(order, processingLog, cancellationToken);

        _logger.LogInformation("Order {OrderId} 進入 Processing 狀態", orderId);

        // ── 3. 樂觀鎖 CAS 重試迴圈 ──────────────────────────────────────────
        var retryCount = 0;

        while (true)
        {
            // 每次迴圈都讀最新的 ticket（AsNoTracking，確保拿到 DB 最新 version）
            var ticket = await _ticketRepository.GetByIdNoTrackingAsync(order.TicketId, cancellationToken);
            if (ticket is null)
            {
                _logger.LogError("Ticket {TicketId} 不存在，訂單 {OrderId} 無法處理", order.TicketId, orderId);
                await TransitionToFailedAsync(order, "ticket_not_found", cancellationToken);
                return;
            }

            // 庫存不足：重試也沒意義，直接失敗
            if (ticket.AvailableQuantity < order.Quantity)
            {
                _logger.LogInformation(
                    "Order {OrderId} 庫存不足（需要 {Quantity}，剩餘 {Available}）",
                    orderId, order.Quantity, ticket.AvailableQuantity);
                await TransitionToFailedAsync(order, "insufficient_inventory", cancellationToken);
                return;
            }

            // 嘗試 CAS 扣庫存
            var affected = await _ticketRepository.TryDeductInventoryAsync(
                order.TicketId, order.Quantity, ticket.Version, cancellationToken);

            if (affected == 1)
            {
                // 成功！
                _logger.LogInformation("Order {OrderId} 扣庫存成功（version {Version}）", orderId, ticket.Version);
                await TransitionToSuccessAsync(order, cancellationToken);
                // TODO(Task 8): invalidate Redis cache after inventory update
                return;
            }

            // version 衝突（有其他請求搶先更新）→ 計數並重試
            retryCount++;
            _logger.LogDebug(
                "Order {OrderId} 樂觀鎖衝突，重試第 {RetryCount} 次",
                orderId, retryCount);

            if (retryCount > MaxRetries)
            {
                _logger.LogWarning(
                    "Order {OrderId} 樂觀鎖重試超過 {MaxRetries} 次，標記為 Failed",
                    orderId, MaxRetries);
                await TransitionToFailedAsync(order, "optimistic_lock_retry_exhausted", cancellationToken);
                return;
            }

            // 繼續迴圈（不 sleep，直接重新讀取最新 version）
        }
    }

    // ── 私有輔助方法 ──────────────────────────────────────────────────────────

    private async Task TransitionToSuccessAsync(Order order, CancellationToken ct)
    {
        var fromStatus = order.Status; // Processing
        order.TransitionTo(OrderStatus.Success, "inventory_deducted");
        var log = OrderStatusLog.Create(order.Id, fromStatus, OrderStatus.Success, "inventory_deducted");
        await _orderRepository.UpdateAndAddStatusLogAsync(order, log, ct);
    }

    private async Task TransitionToFailedAsync(Order order, string reason, CancellationToken ct)
    {
        var fromStatus = order.Status; // Processing
        order.TransitionTo(OrderStatus.Failed, reason);
        var log = OrderStatusLog.Create(order.Id, fromStatus, OrderStatus.Failed, reason);
        await _orderRepository.UpdateAndAddStatusLogAsync(order, log, ct);
    }
}
