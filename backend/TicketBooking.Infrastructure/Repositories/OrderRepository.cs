using Microsoft.EntityFrameworkCore;
using TicketBooking.Application.Interfaces.Repositories;
using TicketBooking.Domain.Entities;
using TicketBooking.Domain.Enums;
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

    public async Task<bool> TryDeductAndTransitionAsync(
        Order order,
        Guid ticketId,
        int quantity,
        int expectedVersion,
        OrderStatus toStatus,
        string reason,
        CancellationToken cancellationToken = default)
    {
        // 在同一個 DB transaction 內執行：
        //   1. CAS 扣庫存（ExecuteUpdateAsync，帶 version 條件）
        //   2. 訂單狀態轉換 + 寫 OrderStatusLog
        // 確保兩者是原子操作，防止「扣庫存成功但訂單狀態未更新」的半完成狀態。
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var affected = await _context.Tickets
                .Where(t => t.Id == ticketId
                         && t.Version == expectedVersion
                         && t.AvailableQuantity >= quantity)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(t => t.AvailableQuantity, t => t.AvailableQuantity - quantity)
                    .SetProperty(t => t.Version, t => t.Version + 1)
                    .SetProperty(t => t.UpdatedAt, _ => DateTime.UtcNow),
                    cancellationToken);

            if (affected != 1)
            {
                // version 衝突或庫存不足，不需要 rollback（沒有任何資料被改變），直接回傳 false
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }

            // 扣庫存成功，執行訂單狀態轉換
            var fromStatus = order.Status;
            order.TransitionTo(toStatus, reason);
            var log = OrderStatusLog.Create(order.Id, fromStatus, toStatus, reason);
            _context.OrderStatusLogs.Add(log);
            await _context.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw; // 讓外層的技術性例外處理邏輯接手（Worker nack）
        }
    }
}
