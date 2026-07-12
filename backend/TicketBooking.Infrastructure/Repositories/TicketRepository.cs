using Microsoft.EntityFrameworkCore;
using TicketBooking.Application.Interfaces.Repositories;
using TicketBooking.Domain.Entities;
using TicketBooking.Infrastructure.Persistence;

namespace TicketBooking.Infrastructure.Repositories;

public class TicketRepository : ITicketRepository
{
    private readonly AppDbContext _context;

    public TicketRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<Ticket>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Tickets
            .OrderBy(t => t.EventStartAt)  // 依活動時間排序，最近的活動在前面
            .ToListAsync(cancellationToken);
    }

    public async Task<Ticket?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Tickets
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
    }

    public async Task<Ticket?> GetByIdNoTrackingAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // 樂觀鎖重試迴圈專用：不加入 EF Core 追蹤，每次必定從 DB 拿到最新的 version 與 available_quantity
        return await _context.Tickets
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
    }

    public async Task<int> TryDeductInventoryAsync(
        Guid ticketId, int quantity, int expectedVersion, CancellationToken cancellationToken = default)
    {
        // 樂觀鎖 CAS：用 EF Core 7+ ExecuteUpdateAsync，帶 version 條件的批次更新。
        // 不經過 EF Core change tracker，直接產生帶參數化的 SQL UPDATE，不需手拼 SQL。
        // 回傳影響筆數：1 = 成功（version 符合且庫存足夠），0 = 失敗
        return await _context.Tickets
            .Where(t => t.Id == ticketId
                     && t.Version == expectedVersion
                     && t.AvailableQuantity >= quantity)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(t => t.AvailableQuantity, t => t.AvailableQuantity - quantity)
                .SetProperty(t => t.Version, t => t.Version + 1)
                .SetProperty(t => t.UpdatedAt, _ => DateTime.UtcNow),
                cancellationToken);
    }
}
