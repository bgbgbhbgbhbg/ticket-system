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
    /// 複合查詢防止跨用戶授權繞過：不同 userId 不會返回對方的訂單
    /// </summary>
    Task<Order?> GetByIdempotencyKeyAsync(string idempotencyKey, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 建立新訂單，回傳資料庫產生 Id 後的完整物件
    /// </summary>
    Task<Order> CreateAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新訂單狀態，並在同一個 DB transaction 內寫入一筆 OrderStatusLog。
    /// 對應 docs/3_specs/domain-state-machine.md 第 5 節「每個轉換都要寫 order_status_logs」的要求。
    /// </summary>
    Task UpdateAndAddStatusLogAsync(Order order, OrderStatusLog log, CancellationToken cancellationToken = default);

    /// <summary>
    /// 在同一個 DB transaction 內執行「CAS 扣庫存 + 訂單狀態轉換 + 寫 OrderStatusLog」。
    /// 防止「扣庫存成功但訂單狀態未更新」的半完成狀態（訊息重投遞時造成重複扣庫存 / 超賣）。
    /// 回傳 true 代表扣庫存成功（affected == 1），false 代表 version 衝突或庫存不足。
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
