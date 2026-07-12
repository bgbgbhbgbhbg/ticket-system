using Microsoft.EntityFrameworkCore;
using TicketBooking.Application.Interfaces.Repositories;
using TicketBooking.Domain.Entities;
using TicketBooking.Infrastructure.Persistence;

namespace TicketBooking.Infrastructure.Repositories;

public class OrderRepository : IOrderRepository
{
    private readonly AppDbContext _context;

    public OrderRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Orders
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
    }

    public async Task<Order?> GetByIdempotencyKeyAsync(string idempotencyKey, Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.Orders
            .FirstOrDefaultAsync(o => o.IdempotencyKey == idempotencyKey && o.UserId == userId, cancellationToken);
    }

    public async Task<Order> CreateAsync(Order order, CancellationToken cancellationToken = default)
    {
        _context.Orders.Add(order);
        await _context.SaveChangesAsync(cancellationToken);
        return order;
    }

    public async Task UpdateAndAddStatusLogAsync(Order order, OrderStatusLog log, CancellationToken cancellationToken = default)
    {
        // Order 是由 GetByIdAsync 取出的被追蹤物件，其屬性變更會被 EF Core 自動偵測（snapshot change tracking），
        // 不需要再呼叫 Update(order)。
        // 新增 OrderStatusLog。
        _context.OrderStatusLogs.Add(log);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
