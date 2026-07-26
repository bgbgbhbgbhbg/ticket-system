using TicketBooking.Domain.Entities;
using TicketBooking.Domain.Enums;

namespace TicketBooking.Application.Interfaces.Repositories;

/// <summary>
/// Order 資料存取 Interface，遵循 Repository Pattern
/// </summary>
public interface IOrderRepository
{
    /// <summary>
    /// 依 ID 查詢訂單（不限使用者，供內部使用）
    /// </summary>
    Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 依 idempotency_key 與 userId 查詢訂單（冪等性檢查）
    /// </summary>
    Task<Order?> GetByIdempotencyKeyAsync(string idempotencyKey, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 分頁查詢所有訂單（Admin 專用），可依狀態篩選。
    /// pageSize 由呼叫端夾住（上限 100）。
    /// </summary>
    Task<(List<Order> Items, int Total)> GetPagedAsync(
        OrderStatus? statusFilter, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// 建立新訂單，回傳資料庫產生 Id 後的完整物件
    /// </summary>
    Task<Order> CreateAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新訂單狀態，並在同一個 DB transaction 內寫入一筆 OrderStatusLog。
    /// </summary>
    Task UpdateAndAddStatusLogAsync(Order order, OrderStatusLog log, CancellationToken cancellationToken = default);

    /// <summary>
    /// 在同一個 DB transaction 內執行「CAS 扣庫存 + 訂單狀態轉換 + 寫 OrderStatusLog」。
    /// </summary>
    Task<bool> TryDeductAndTransitionAsync(
        Order order,
        Guid ticketId,
        int quantity,
        int expectedVersion,
        OrderStatus toStatus,
        string reason,
        CancellationToken cancellationToken = default);
}
