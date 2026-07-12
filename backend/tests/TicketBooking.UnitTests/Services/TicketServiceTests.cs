using NSubstitute;
using TicketBooking.Application.Interfaces.Repositories;
using TicketBooking.Application.Services;
using TicketBooking.Domain.Entities;

namespace TicketBooking.UnitTests.Services;

public class TicketServiceTests
{
    private readonly ITicketRepository _ticketRepository;
    private readonly TicketService _ticketService;

    public TicketServiceTests()
    {
        // 使用 NSubstitute 建立 mock repository
        _ticketRepository = Substitute.For<ITicketRepository>();
        _ticketService = new TicketService(_ticketRepository);
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
                DateTime.Parse("2026-08-15 19:00:00"),
                100,
                3500.00m
            ),
            Ticket.Create(
                "搖滾區站票",
                "五月天演唱會 2026 台北站",
                DateTime.Parse("2026-08-15 19:00:00"),
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
            DateTime.Parse("2026-08-15 19:00:00"),
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
            DateTime.Parse("2026-08-15 19:00:00"),
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
}
