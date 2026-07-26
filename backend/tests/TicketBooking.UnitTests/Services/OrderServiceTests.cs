using NSubstitute;
using TicketBooking.Application.Exceptions;
using TicketBooking.Application.Interfaces;
using TicketBooking.Application.Interfaces.Repositories;
using TicketBooking.Application.Services;
using TicketBooking.Domain.Entities;
using TicketBooking.Domain.Enums;
using TicketBooking.Domain.Exceptions;

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
    // UT-ORD-02B: TOCTOU 並發場景 — CreateAsync 拋 DuplicateIdempotencyKeyException
    //             → 重查回傳既有訂單（IsNew = false），不 publish
    // ─────────────────────────────────────────────────────────
    [Fact]
    public async Task CreateOrderAsync_ConcurrentDuplicateInsert_ShouldReturnExistingOrderWithoutPublishing()
    {
        // Arrange：第一次 idempotency 查詢回傳 null（通過 TOCTOU 窗口），
        //           但 CreateAsync 因並發衝突拋 DuplicateIdempotencyKeyException；
        //           第二次查詢回傳競爭者已建立的訂單。
        var idempotencyKey = "concurrent-key-456";
        var competingOrder = Order.Create(UserId, TicketId, 1, 500m, idempotencyKey);
        var ticket = MakeTicket(price: 500m);

        _orderRepository.GetByIdempotencyKeyAsync(idempotencyKey, UserId, Arg.Any<CancellationToken>())
            .Returns((Order?)null, competingOrder);  // 第一次 null，第二次回傳競爭者訂單

        _ticketRepository.GetByIdAsync(TicketId, Arg.Any<CancellationToken>())
            .Returns(ticket);

        _orderRepository.CreateAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<Order>(new DuplicateIdempotencyKeyException()));

        // Act
        var (order, isNew) = await _orderService.CreateOrderAsync(UserId, TicketId, 1, idempotencyKey);

        // Assert：應回傳既有訂單、IsNew = false
        Assert.False(isNew);
        Assert.Equal(competingOrder, order);

        // CreateAsync 被呼叫一次（嘗試插入）
        await _orderRepository.Received(1).CreateAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());

        // GetByIdempotencyKeyAsync 被呼叫兩次（第一次通過 + 第二次重查）
        await _orderRepository.Received(2).GetByIdempotencyKeyAsync(
            idempotencyKey, UserId, Arg.Any<CancellationToken>());

        // 不應該 publish（不是新訂單，且不能重複發送 MQ 訊息）
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

    // ═════════════════════════════════════════════════════════
    // Admin 方法
    // ═════════════════════════════════════════════════════════

    // ─────────────────────────────────────────────────────────
    // UT-ORD-09: GetOrdersAsync 正常分頁查詢
    // ─────────────────────────────────────────────────────────
    [Fact]
    public async Task GetOrdersAsync_ValidParams_ShouldReturnPagedOrders()
    {
        // Arrange
        var orders = new List<Order>
        {
            Order.Create(UserId, TicketId, 1, 500m, "key-1"),
            Order.Create(UserId, TicketId, 2, 1000m, "key-2"),
        };
        _orderRepository.GetPagedAsync(null, 1, 20, Arg.Any<CancellationToken>())
            .Returns((orders, 2));

        // Act
        var (items, total) = await _orderService.GetOrdersAsync(null, 1, 20);

        // Assert
        Assert.Equal(2, total);
        Assert.Equal(2, items.Count);
        await _orderRepository.Received(1).GetPagedAsync(null, 1, 20, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0, 1)]    // page 最小夾住為 1
    [InlineData(200, 100)] // pageSize 夾住為 100
    public async Task GetOrdersAsync_OutOfBoundsParams_ShouldClamp(int pageSize, int expectedPageSize)
    {
        // Arrange
        _orderRepository.GetPagedAsync(null, 1, expectedPageSize, Arg.Any<CancellationToken>())
            .Returns((new List<Order>(), 0));

        // Act
        await _orderService.GetOrdersAsync(null, 1, pageSize);

        // Assert：Repository 被呼叫時 pageSize 已夾住
        await _orderRepository.Received(1).GetPagedAsync(null, 1, expectedPageSize, Arg.Any<CancellationToken>());
    }

    // ─────────────────────────────────────────────────────────
    // UT-ORD-10: UpdateOrderStatusAsync 正常轉換
    // ─────────────────────────────────────────────────────────
    [Fact]
    public async Task UpdateOrderStatusAsync_ValidTransition_ShouldUpdateAndLog()
    {
        // Arrange：Processing → Success（合法轉換）
        var orderId = Guid.NewGuid();
        var order = Order.Create(UserId, TicketId, 1, 500m, "key-admin-1");
        order.TransitionTo(OrderStatus.Processing, "worker_picked_up");

        _orderRepository.GetByIdAsync(orderId, Arg.Any<CancellationToken>())
            .Returns(order);
        _orderRepository.UpdateAndAddStatusLogAsync(
            Arg.Any<Order>(), Arg.Any<OrderStatusLog>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Act
        var result = await _orderService.UpdateOrderStatusAsync(
            orderId, OrderStatus.Success, "admin_manual_override: 手動確認成功");

        // Assert
        Assert.Equal(OrderStatus.Success, result.Status);
        await _orderRepository.Received(1).UpdateAndAddStatusLogAsync(
            Arg.Any<Order>(), Arg.Any<OrderStatusLog>(), Arg.Any<CancellationToken>());
    }

    // ─────────────────────────────────────────────────────────
    // UT-ORD-11: UpdateOrderStatusAsync 訂單不存在 → OrderNotFoundException
    // ─────────────────────────────────────────────────────────
    [Fact]
    public async Task UpdateOrderStatusAsync_OrderNotFound_ShouldThrow()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        _orderRepository.GetByIdAsync(orderId, Arg.Any<CancellationToken>())
            .Returns((Order?)null);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<OrderNotFoundException>(() =>
            _orderService.UpdateOrderStatusAsync(orderId, OrderStatus.Success, "admin_manual_override: test"));

        Assert.Equal(orderId, ex.OrderId);
        await _orderRepository.DidNotReceive().UpdateAndAddStatusLogAsync(
            Arg.Any<Order>(), Arg.Any<OrderStatusLog>(), Arg.Any<CancellationToken>());
    }

    // ─────────────────────────────────────────────────────────
    // UT-ORD-12: UpdateOrderStatusAsync 終態不可逆（Success → Failed）→ InvalidStatusTransitionException
    // ─────────────────────────────────────────────────────────
    [Fact]
    public async Task UpdateOrderStatusAsync_TerminalStateTransition_ShouldThrow()
    {
        // Arrange：訂單已是 Success（終態），Admin 嘗試改成 Failed
        var orderId = Guid.NewGuid();
        var order = Order.Create(UserId, TicketId, 1, 500m, "key-admin-2");
        order.TransitionTo(OrderStatus.Processing, "worker_picked_up");
        order.TransitionTo(OrderStatus.Success, "inventory_deducted");

        _orderRepository.GetByIdAsync(orderId, Arg.Any<CancellationToken>())
            .Returns(order);

        // Act & Assert：TransitionTo 內部拋出 InvalidStatusTransitionException
        await Assert.ThrowsAsync<InvalidStatusTransitionException>(() =>
            _orderService.UpdateOrderStatusAsync(orderId, OrderStatus.Failed, "admin_manual_override: test"));

        // 不應該呼叫 UpdateAndAddStatusLogAsync（轉換失敗，無需寫 DB）
        await _orderRepository.DidNotReceive().UpdateAndAddStatusLogAsync(
            Arg.Any<Order>(), Arg.Any<OrderStatusLog>(), Arg.Any<CancellationToken>());
    }
}

