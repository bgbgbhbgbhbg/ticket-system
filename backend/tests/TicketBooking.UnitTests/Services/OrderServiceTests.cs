using NSubstitute;
using TicketBooking.Application.Exceptions;
using TicketBooking.Application.Interfaces;
using TicketBooking.Application.Interfaces.Repositories;
using TicketBooking.Application.Services;
using TicketBooking.Domain.Entities;
using TicketBooking.Domain.Enums;

namespace TicketBooking.UnitTests.Services;

public class OrderServiceTests
{
    private readonly IOrderRepository _orderRepository;
    private readonly ITicketRepository _ticketRepository;
    private readonly IMessagePublisher _messagePublisher;
    private readonly OrderService _orderService;

    // 測試用固定 ID
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid TicketId = Guid.NewGuid();

    private static Ticket MakeTicket(decimal price = 500m, int availableQuantity = 100)
    {
        // Ticket.Create() 不接受 availableQuantity，只能透過 Create 建立（反映真實約束）
        var ticket = Ticket.Create("VIP 區", "演唱會", DateTime.UtcNow.AddDays(30), availableQuantity, price);
        return ticket;
    }

    public OrderServiceTests()
    {
        _orderRepository = Substitute.For<IOrderRepository>();
        _ticketRepository = Substitute.For<ITicketRepository>();
        _messagePublisher = Substitute.For<IMessagePublisher>();

        _orderService = new OrderService(_orderRepository, _ticketRepository, _messagePublisher);
    }

    // ─────────────────────────────────────────────────────────
    // UT-ORD-01: 正常建立訂單
    // ─────────────────────────────────────────────────────────
    [Fact]
    public async Task CreateOrderAsync_ValidRequest_ShouldCreateOrderAndPublishMessage()
    {
        // Arrange
        var ticket = MakeTicket(price: 500m);
        var idempotencyKey = Guid.NewGuid().ToString();
        var quantity = 2;

        _orderRepository.GetByIdempotencyKeyAsync(idempotencyKey, UserId, Arg.Any<CancellationToken>())
            .Returns((Order?)null);
        _ticketRepository.GetByIdAsync(TicketId, Arg.Any<CancellationToken>())
            .Returns(ticket);

        var savedOrder = Order.Create(UserId, TicketId, quantity, 1000m, idempotencyKey);
        _orderRepository.CreateAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>())
            .Returns(savedOrder);

        // Act
        var (order, isNew) = await _orderService.CreateOrderAsync(UserId, TicketId, quantity, idempotencyKey);

        // Assert
        Assert.True(isNew);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(1000m, order.TotalAmount); // quantity(2) × price(500)

        await _orderRepository.Received(1).CreateAsync(
            Arg.Is<Order>(o =>
                o.UserId == UserId &&
                o.TicketId == TicketId &&
                o.Quantity == quantity &&
                o.TotalAmount == 1000m &&
                o.IdempotencyKey == idempotencyKey &&
                o.Status == OrderStatus.Pending),
            Arg.Any<CancellationToken>());

        await _messagePublisher.Received(1).PublishOrderCreatedAsync(
            savedOrder.Id, UserId, TicketId, quantity, Arg.Any<CancellationToken>());
    }

    // ─────────────────────────────────────────────────────────
    // UT-ORD-02: Idempotency-Key 重複 → 回傳原訂單（IsNew = false）
    // ─────────────────────────────────────────────────────────
    [Fact]
    public async Task CreateOrderAsync_DuplicateIdempotencyKey_ShouldReturnExistingOrder()
    {
        // Arrange
        var idempotencyKey = "duplicate-key-123";
        var existingOrder = Order.Create(UserId, TicketId, 1, 500m, idempotencyKey);

        _orderRepository.GetByIdempotencyKeyAsync(idempotencyKey, UserId, Arg.Any<CancellationToken>())
            .Returns(existingOrder);

        // Act
        var (order, isNew) = await _orderService.CreateOrderAsync(UserId, TicketId, 1, idempotencyKey);

        // Assert
        Assert.False(isNew);
        Assert.Equal(existingOrder, order);

        // 不應該查 ticket、不應該新建訂單、不應該發送訊息
        await _ticketRepository.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _orderRepository.DidNotReceive().CreateAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
        await _messagePublisher.DidNotReceive().PublishOrderCreatedAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    // ─────────────────────────────────────────────────────────
    // UT-ORD-03: quantity 超過 10 → OrderQuantityExceedsLimitException
    // ─────────────────────────────────────────────────────────
    [Theory]
    [InlineData(11)]
    [InlineData(100)]
    [InlineData(int.MaxValue)]
    public async Task CreateOrderAsync_QuantityExceedsLimit_ShouldThrow(int quantity)
    {
        // Arrange
        var idempotencyKey = Guid.NewGuid().ToString();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<OrderQuantityExceedsLimitException>(() =>
            _orderService.CreateOrderAsync(UserId, TicketId, quantity, idempotencyKey));

        Assert.Equal(quantity, ex.RequestedQuantity);
        Assert.Equal(10, ex.MaxQuantity);

        // 不應該觸發任何 repository 或 publisher 呼叫
        await _orderRepository.DidNotReceive().GetByIdempotencyKeyAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _ticketRepository.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _messagePublisher.DidNotReceive().PublishOrderCreatedAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    // ─────────────────────────────────────────────────────────
    // UT-ORD-04: ticketId 查無此票券 → TicketNotFoundException
    // ─────────────────────────────────────────────────────────
    [Fact]
    public async Task CreateOrderAsync_TicketNotFound_ShouldThrow()
    {
        // Arrange
        var idempotencyKey = Guid.NewGuid().ToString();
        var nonExistentTicketId = Guid.NewGuid();

        _orderRepository.GetByIdempotencyKeyAsync(idempotencyKey, UserId, Arg.Any<CancellationToken>())
            .Returns((Order?)null);
        _ticketRepository.GetByIdAsync(nonExistentTicketId, Arg.Any<CancellationToken>())
            .Returns((Ticket?)null);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<TicketNotFoundException>(() =>
            _orderService.CreateOrderAsync(UserId, nonExistentTicketId, 1, idempotencyKey));

        Assert.Equal(nonExistentTicketId, ex.TicketId);

        await _orderRepository.DidNotReceive().CreateAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
        await _messagePublisher.DidNotReceive().PublishOrderCreatedAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    // ─────────────────────────────────────────────────────────
    // UT-ORD-05: TotalAmount 快照正確（quantity × ticket.Price）
    // ─────────────────────────────────────────────────────────
    [Theory]
    [InlineData(1, 1000, 1000)]
    [InlineData(3, 1500, 4500)]
    [InlineData(10, 250, 2500)]
    public async Task CreateOrderAsync_TotalAmountIsSnapshot(int quantity, int priceInt, int expectedTotalInt)
    {
        // Arrange
        var price = (decimal)priceInt;
        var expectedTotal = (decimal)expectedTotalInt;
        var ticket = MakeTicket(price: price);
        var idempotencyKey = Guid.NewGuid().ToString();

        _orderRepository.GetByIdempotencyKeyAsync(idempotencyKey, UserId, Arg.Any<CancellationToken>())
            .Returns((Order?)null);
        _ticketRepository.GetByIdAsync(TicketId, Arg.Any<CancellationToken>())
            .Returns(ticket);

        Order? capturedOrder = null;
        _orderRepository.CreateAsync(Arg.Do<Order>(o => capturedOrder = o), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<Order>());

        // Act
        await _orderService.CreateOrderAsync(UserId, TicketId, quantity, idempotencyKey);

        // Assert
        Assert.NotNull(capturedOrder);
        Assert.Equal(expectedTotal, capturedOrder!.TotalAmount);
    }

    // ─────────────────────────────────────────────────────────
    // UT-ORD-06: GetOrderByIdAsync - 屬於自己的訂單正常回傳
    // ─────────────────────────────────────────────────────────
    [Fact]
    public async Task GetOrderByIdAsync_OwnOrder_ShouldReturnOrder()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var order = Order.Create(UserId, TicketId, 1, 500m, "some-key");

        _orderRepository.GetByIdAsync(orderId, Arg.Any<CancellationToken>())
            .Returns(order);

        // Act
        var result = await _orderService.GetOrderByIdAsync(orderId, UserId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(order, result);
    }

    // ─────────────────────────────────────────────────────────
    // UT-ORD-07: GetOrderByIdAsync - 訂單不屬於此使用者 → 回傳 null
    // ─────────────────────────────────────────────────────────
    [Fact]
    public async Task GetOrderByIdAsync_OtherUsersOrder_ShouldReturnNull()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var order = Order.Create(otherUserId, TicketId, 1, 500m, "some-key");

        _orderRepository.GetByIdAsync(orderId, Arg.Any<CancellationToken>())
            .Returns(order);

        // Act
        var result = await _orderService.GetOrderByIdAsync(orderId, UserId);

        // Assert
        Assert.Null(result);
    }

    // ─────────────────────────────────────────────────────────
    // UT-ORD-08: GetOrderByIdAsync - 訂單不存在 → 回傳 null
    // ─────────────────────────────────────────────────────────
    [Fact]
    public async Task GetOrderByIdAsync_NotFound_ShouldReturnNull()
    {
        // Arrange
        var orderId = Guid.NewGuid();

        _orderRepository.GetByIdAsync(orderId, Arg.Any<CancellationToken>())
            .Returns((Order?)null);

        // Act
        var result = await _orderService.GetOrderByIdAsync(orderId, UserId);

        // Assert
        Assert.Null(result);
    }
}
