using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using TicketBooking.Application.Exceptions;
using TicketBooking.Application.Interfaces.Caching;
using TicketBooking.Application.Interfaces.Repositories;
using TicketBooking.Application.Services;
using TicketBooking.Domain.Entities;

namespace TicketBooking.UnitTests.Services;

public class TicketServiceTests
{
    private readonly ITicketRepository _ticketRepository;
    private readonly ICacheService _cacheService;
    private readonly ILogger<TicketService> _logger;
    private readonly TicketService _ticketService;

    public TicketServiceTests()
    {
        _ticketRepository = Substitute.For<ITicketRepository>();
        _cacheService = Substitute.For<ICacheService>();
        _logger = Substitute.For<ILogger<TicketService>>();
        _ticketService = new TicketService(_ticketRepository, _cacheService, _logger);
    }

    [Fact]
    public async Task GetAllTicketsAsync_ReturnsAllTickets()
    {
        // Arrange: 準備測試資料
        var expectedTickets = new List<Ticket>
        {
            Ticket.Create(
                "VIP 區前排",
                "五月天演唱會 2026 台北站",
                new DateTime(2026, 8, 15, 19, 0, 0),
                100,
                3500.00m
            ),
            Ticket.Create(
                "搖滾區站票",
                "五月天演唱會 2026 台北站",
                new DateTime(2026, 8, 15, 19, 0, 0),
                500,
                2000.00m
            )
        };

        // 設定 mock 行為：當呼叫 GetAllAsync 時，回傳預期的資料
        _ticketRepository.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(expectedTickets);

        // Act: 執行被測試的方法
        var result = await _ticketService.GetAllTicketsAsync();

        // Assert: 驗證結果
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Equal("VIP 區前排", result[0].Name);
        Assert.Equal("搖滾區站票", result[1].Name);

        // 驗證 repository 的方法被正確呼叫
        await _ticketRepository.Received(1).GetAllAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAllTicketsAsync_WhenNoTickets_ReturnsEmptyList()
    {
        // Arrange
        _ticketRepository.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(new List<Ticket>());

        // Act
        var result = await _ticketService.GetAllTicketsAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetTicketByIdAsync_ExistingId_ReturnsTicket()
    {
        // Arrange
        var ticketId = Guid.NewGuid();
        var expectedTicket = Ticket.Create(
            "VIP 區前排",
            "五月天演唱會 2026 台北站",
            new DateTime(2026, 8, 15, 19, 0, 0),
            100,
            3500.00m
        );

        _ticketRepository.GetByIdAsync(ticketId, Arg.Any<CancellationToken>())
            .Returns(expectedTicket);

        // Act
        var result = await _ticketService.GetTicketByIdAsync(ticketId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("VIP 區前排", result.Name);
        Assert.Equal("五月天演唱會 2026 台北站", result.EventName);

        await _ticketRepository.Received(1).GetByIdAsync(ticketId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetTicketByIdAsync_NonExistingId_ReturnsNull()
    {
        // Arrange
        var ticketId = Guid.NewGuid();

        _ticketRepository.GetByIdAsync(ticketId, Arg.Any<CancellationToken>())
            .Returns((Ticket?)null);

        // Act
        var result = await _ticketService.GetTicketByIdAsync(ticketId);

        // Assert
        Assert.Null(result);

        await _ticketRepository.Received(1).GetByIdAsync(ticketId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetTicketByIdAsync_VerifyPriceAndQuantity()
    {
        // Arrange
        var ticketId = Guid.NewGuid();
        var expectedTicket = Ticket.Create(
            "搖滾區站票",
            "五月天演唱會 2026 台北站",
            new DateTime(2026, 8, 15, 19, 0, 0),
            500,
            2000.00m
        );

        _ticketRepository.GetByIdAsync(ticketId, Arg.Any<CancellationToken>())
            .Returns(expectedTicket);

        // Act
        var result = await _ticketService.GetTicketByIdAsync(ticketId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2000.00m, result.Price);
        Assert.Equal(500, result.TotalQuantity);
        Assert.Equal(500, result.AvailableQuantity);  // Create 時 AvailableQuantity = TotalQuantity
    }

    // ── GetInventoryAsync 測試（Task 8 Cache-Aside）────────────────────────────

    /// <summary>
    /// UT-CAC-01：Cache Hit — GetAsync 有值時直接回傳，不查 DB。
    /// </summary>
    [Fact]
    public async Task GetInventoryAsync_CacheHit_ReturnsCachedValueWithoutQueryingDb()
    {
        // Arrange
        var ticketId = Guid.NewGuid();
        _cacheService.GetAsync($"ticket:{ticketId}:inventory", Arg.Any<CancellationToken>())
            .Returns("42");

        // Act
        var (qty, cacheHit) = await _ticketService.GetInventoryAsync(ticketId);

        // Assert
        Assert.Equal(42, qty);
        Assert.True(cacheHit);
        // cache hit 時不應查 DB
        await _ticketRepository.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// UT-CAC-02：Cache Miss — GetAsync 回傳 null 時查 DB，寫回 cache，CacheHit=false。
    /// </summary>
    [Fact]
    public async Task GetInventoryAsync_CacheMiss_QueriesDbAndSetsCache()
    {
        // Arrange
        var ticketId = Guid.NewGuid();
        _cacheService.GetAsync($"ticket:{ticketId}:inventory", Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var ticket = Ticket.Create("VIP", "演唱會", DateTime.UtcNow, 100, 500m);
        _ticketRepository.GetByIdAsync(ticketId, Arg.Any<CancellationToken>())
            .Returns(ticket);

        // Act
        var (qty, cacheHit) = await _ticketService.GetInventoryAsync(ticketId);

        // Assert
        Assert.Equal(100, qty);
        Assert.False(cacheHit);
        // 查完 DB 後應寫回 cache（ttl=null，靠主動 invalidate）
        await _cacheService.Received(1).SetAsync(
            $"ticket:{ticketId}:inventory",
            "100",
            null,
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// UT-CAC-03：票券不存在 — 查 DB 找不到時應拋出 TicketNotFoundException。
    /// </summary>
    [Fact]
    public async Task GetInventoryAsync_TicketNotFound_ThrowsTicketNotFoundException()
    {
        // Arrange
        var ticketId = Guid.NewGuid();
        _cacheService.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);
        _ticketRepository.GetByIdAsync(ticketId, Arg.Any<CancellationToken>())
            .Returns((Ticket?)null);

        // Act & Assert
        await Assert.ThrowsAsync<TicketNotFoundException>(
            () => _ticketService.GetInventoryAsync(ticketId));
    }

    /// <summary>
    /// UT-CAC-04：降級行為 — ICacheService.GetAsync 拋例外時，降級查 DB，API 仍正常回傳。
    /// 對應 cache-strategy.md 第 5 節。
    /// </summary>
    [Fact]
    public async Task GetInventoryAsync_CacheGetThrows_FallsBackToDb()
    {
        // Arrange
        var ticketId = Guid.NewGuid();
        _cacheService.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Redis connection refused"));

        var ticket = Ticket.Create("VIP", "演唱會", DateTime.UtcNow, 200, 500m);
        _ticketRepository.GetByIdAsync(ticketId, Arg.Any<CancellationToken>())
            .Returns(ticket);

        // Act — 不應拋出例外
        var (qty, cacheHit) = await _ticketService.GetInventoryAsync(ticketId);

        // Assert
        Assert.Equal(200, qty);
        Assert.False(cacheHit);
        await _ticketRepository.Received(1).GetByIdAsync(ticketId, Arg.Any<CancellationToken>());
    }
}
