using TicketBooking.Domain.Entities;

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
}
