using TicketBooking.Domain.Entities;
using TicketBooking.Domain.Enums;

namespace TicketBooking.Application.Interfaces.Services;

/// <summary>
/// Order 業務邏輯 Service Interface
/// </summary>
public interface IOrderService
{
    /// <summary>
    /// 建立訂單（搶票入口）。
    /// 若 idempotencyKey 已存在，回傳 (既有訂單, false)；
    /// 若成功建立新訂單，回傳 (新訂單, true)。
    /// </summary>
    Task<(Order Order, bool IsNew)> CreateOrderAsync(
        Guid userId,
        Guid ticketId,
        int quantity,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 查詢單筆訂單（只能查看屬於自己的訂單）
    /// </summary>
    Task<Order?> GetOrderByIdAsync(
        Guid orderId,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Admin 分頁查詢所有訂單，可依狀態篩選。
    /// </summary>
    Task<(List<Order> Items, int Total)> GetOrdersAsync(
        OrderStatus? statusFilter, int page, int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Admin 手動介入訂單狀態。
    /// 仍須通過 Order.TransitionTo() 合法轉換檢查。
    /// 拋出 OrderNotFoundException（查無訂單）或 InvalidStatusTransitionException（不合法轉換）。
    /// </summary>
    Task<Order> UpdateOrderStatusAsync(
        Guid orderId, OrderStatus toStatus, string reason,
        CancellationToken cancellationToken = default);
}
