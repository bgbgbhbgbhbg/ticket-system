using Microsoft.Extensions.Logging;
using TicketBooking.Application.Exceptions;
using TicketBooking.Application.Interfaces.Caching;
using TicketBooking.Application.Interfaces.Repositories;
using TicketBooking.Application.Interfaces.Services;
using TicketBooking.Domain.Entities;

namespace TicketBooking.Application.Services;

public class TicketService : ITicketService
{
    private readonly ITicketRepository _ticketRepository;
    private readonly ICacheService _cacheService;
    private readonly ILogger<TicketService> _logger;

    public TicketService(
        ITicketRepository ticketRepository,
        ICacheService cacheService,
        ILogger<TicketService> logger)
    {
        _ticketRepository = ticketRepository;
        _cacheService = cacheService;
        _logger = logger;
    }

    public async Task<List<Ticket>> GetAllTicketsAsync(CancellationToken cancellationToken = default)
    {
        return await _ticketRepository.GetAllAsync(cancellationToken);
    }

    public async Task<Ticket?> GetTicketByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _ticketRepository.GetByIdAsync(id, cancellationToken);
    }

    /// <summary>
    /// Cache-Aside 庫存查詢。對應 docs/3_specs/cache-strategy.md 第 3 節。
    /// Redis 故障時降級直接查 DB，不讓 Redis 異常影響 API 可用性（第 5 節）。
    /// </summary>
    public async Task<(int AvailableQuantity, bool CacheHit)> GetInventoryAsync(
        Guid ticketId, CancellationToken cancellationToken = default)
    {
        // ── 1. 嘗試讀取 cache（Redis 故障時視同 cache miss）────────────────
        try
        {
            var cached = await _cacheService.GetAsync($"ticket:{ticketId}:inventory", cancellationToken);
            if (cached is not null)
            {
                return (int.Parse(cached), true);
            }
        }
        catch (Exception ex)
        {
            // Redis 故障降級：視同 cache miss，繼續查 DB
            _logger.LogWarning(ex, "Cache GetAsync failed for ticket {TicketId}, falling back to DB", ticketId);
        }

        // ── 2. Cache miss → 查 DB ─────────────────────────────────────────
        var ticket = await _ticketRepository.GetByIdAsync(ticketId, cancellationToken);
        if (ticket is null)
        {
            throw new TicketNotFoundException(ticketId);
        }

        // ── 3. 寫回 cache（Redis 故障時靜默忽略）────────────────────────────
        try
        {
            await _cacheService.SetAsync(
                $"ticket:{ticketId}:inventory",
                ticket.AvailableQuantity.ToString(),
                ttl: null,    // 庫存不設 TTL，靠訂單處理完成後主動 DEL
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache SetAsync failed for ticket {TicketId}", ticketId);
        }

        return (ticket.AvailableQuantity, false);
    }
}
