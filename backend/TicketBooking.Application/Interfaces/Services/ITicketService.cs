using TicketBooking.Domain.Entities;

namespace TicketBooking.Application.Interfaces.Services;

/// <summary>
/// Ticket 業務邏輯 Service Interface
/// </summary>
public interface ITicketService
{
    /// <summary>
    /// 取得所有票券列表
    /// </summary>
    Task<List<Ticket>> GetAllTicketsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 取得單一票券詳情
    /// </summary>
    /// <returns>找到則回傳 Ticket，找不到回傳 null</returns>
    Task<Ticket?> GetTicketByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 查詢票券即時庫存（Cache-Aside 模式）。
    /// 對應 docs/3_specs/cache-strategy.md 第 3 節。
    /// </summary>
    /// <returns>
    /// AvailableQuantity: 剩餘庫存。
    /// CacheHit: true 表示資料來自 Redis cache，false 表示直接查 DB。
    /// </returns>
    /// <exception cref="TicketBooking.Application.Exceptions.TicketNotFoundException">找不到票券時拋出</exception>
    Task<(int AvailableQuantity, bool CacheHit)> GetInventoryAsync(Guid ticketId, CancellationToken cancellationToken = default);
}
