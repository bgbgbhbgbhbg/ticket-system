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
}
