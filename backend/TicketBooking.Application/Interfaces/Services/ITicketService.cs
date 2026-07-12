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
}
