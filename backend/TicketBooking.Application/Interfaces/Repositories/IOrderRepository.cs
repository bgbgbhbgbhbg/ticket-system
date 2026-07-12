using TicketBooking.Domain.Entities;

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
}
