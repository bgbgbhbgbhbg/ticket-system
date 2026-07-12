using TicketBooking.Domain.Entities;

namespace TicketBooking.Application.Interfaces.Repositories;

/// <summary>
/// Ticket 資料存取 Interface，遵循 Repository Pattern
/// </summary>
public interface ITicketRepository
{
    /// <summary>
    /// 查詢所有票券
    /// </summary>
    Task<List<Ticket>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 依 ID 查詢單一票券
    /// </summary>
    Task<Ticket?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 依 ID 查詢單一票券（AsNoTracking，不加入 EF Core 追蹤）。
    /// 樂觀鎖重試迴圈中用這個，每次都能拿到資料庫最新的 version 與 available_quantity。
    /// </summary>
    Task<Ticket?> GetByIdNoTrackingAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 樂觀鎖 CAS 扣庫存（對應 docs/3_specs/data-model.md 2.2 節）。
    /// 用 EF Core ExecuteUpdateAsync 帶 version 條件，回傳影響筆數：
    ///   1 = 成功（version 符合、庫存足夠）
    ///   0 = 失敗（version 不符或庫存不足，需由呼叫端重新讀取 ticket 判斷原因）
    /// </summary>
    Task<int> TryDeductInventoryAsync(Guid ticketId, int quantity, int expectedVersion, CancellationToken cancellationToken = default);
}
