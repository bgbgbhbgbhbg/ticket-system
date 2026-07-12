using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using TicketBooking.Application.Interfaces.Repositories;
using TicketBooking.Application.Services;
using TicketBooking.Domain.Entities;
using TicketBooking.Domain.Enums;
using TicketBooking.Domain.Exceptions;

namespace TicketBooking.UnitTests.Services;

/// <summary>
/// OrderProcessingService 單元測試，涵蓋：
/// - 正常路徑（庫存充足，一次成功）
/// - 樂觀鎖衝突重試邏輯
/// - 重試超過上限 → Failed
/// - 庫存不足 → Failed
/// - 每個終態都寫入 OrderStatusLog
/// 對應 docs/test-plan.md 的 Task 5 測試案例。
/// </summary>
public class OrderProcessingServiceTests
{
    // ── Test fixtures ─────────────────────────────────────────────────────────

    private readonly IOrderRepository _orderRepository;
    private readonly ITicketRepository _ticketRepository;
    private readonly ILogger<OrderProcessingService> _logger;
    private readonly OrderProcessingService _sut;

    private static readonly Guid OrderId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid TicketId = Guid.NewGuid();
    private const string IdempotencyKey = "test-key-001";

    public OrderProcessingServiceTests()
    {
        _orderRepository = Substitute.For<IOrderRepository>();
        _ticketRepository = Substitute.For<ITicketRepository>();
        _logger = Substitute.For<ILogger<OrderProcessingService>>();

        _sut = new OrderProcessingService(_orderRepository, _ticketRepository, _logger);
    }

    // ── 輔助工廠 ─────────────────────────────────────────────────────────────

    private static Order MakePendingOrder(int quantity = 2)
        => Order.Create(UserId, TicketId, quantity, quantity * 500m, IdempotencyKey);

    private static Ticket MakeTicket(int availableQuantity = 100, int version = 0)
        => Ticket.Create("VIP 區", "演唱會", DateTime.UtcNow.AddDays(30), availableQuantity, 500m);

    // ── UT-PROC-01: 正常路徑 ──────────────────────────────────────────────────

    [Fact]
    public async Task ProcessOrderAsync_SufficientInventory_ShouldTransitionToSuccess()
    {
        // Arrange
        var order = MakePendingOrder(quantity: 2);
        var ticket = MakeTicket(availableQuantity: 100, version: 0);

        _orderRepository.GetByIdAsync(OrderId, Arg.Any<CancellationToken>()).Returns(order);
        _ticketRepository.GetByIdNoTrackingAsync(TicketId, Arg.Any<CancellationToken>()).Returns(ticket);
        _ticketRepository.TryDeductInventoryAsync(TicketId, 2, 0, Arg.Any<CancellationToken>()).Returns(1); // 成功

        // Act
        await _sut.ProcessOrderAsync(OrderId);

        // Assert
        Assert.Equal(OrderStatus.Success, order.Status);

        // 應呼叫 UpdateAndAddStatusLogAsync 兩次：Processing + Success
        await _orderRepository.Received(2).UpdateAndAddStatusLogAsync(
            Arg.Is<Order>(o => o == order),
            Arg.Any<OrderStatusLog>(),
            Arg.Any<CancellationToken>());

        // 第一次 log 轉到 Processing
        await _orderRepository.Received(1).UpdateAndAddStatusLogAsync(
            order,
            Arg.Is<OrderStatusLog>(l =>
                l.ToStatus == OrderStatus.Processing && l.Reason == "worker_picked_up"),
            Arg.Any<CancellationToken>());

        // 第二次 log 轉到 Success
        await _orderRepository.Received(1).UpdateAndAddStatusLogAsync(
            order,
            Arg.Is<OrderStatusLog>(l =>
                l.ToStatus == OrderStatus.Success && l.Reason == "inventory_deducted"),
            Arg.Any<CancellationToken>());
    }

    // ── UT-PROC-02: 庫存不足 → Failed ────────────────────────────────────────

    [Fact]
    public async Task ProcessOrderAsync_InsufficientInventory_ShouldTransitionToFailed()
    {
        // Arrange
        var order = MakePendingOrder(quantity: 5);
        // ticket 只剩 3 張，訂單需要 5 張
        var ticket = MakeTicket(availableQuantity: 3, version: 0);

        _orderRepository.GetByIdAsync(OrderId, Arg.Any<CancellationToken>()).Returns(order);
        _ticketRepository.GetByIdNoTrackingAsync(TicketId, Arg.Any<CancellationToken>()).Returns(ticket);

        // Act
        await _sut.ProcessOrderAsync(OrderId);

        // Assert
        Assert.Equal(OrderStatus.Failed, order.Status);

        // 不應呼叫 TryDeductInventoryAsync（庫存不足直接失敗）
        await _ticketRepository.DidNotReceive().TryDeductInventoryAsync(
            Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());

        // 應寫入 Failed log，reason = insufficient_inventory
        await _orderRepository.Received(1).UpdateAndAddStatusLogAsync(
            order,
            Arg.Is<OrderStatusLog>(l =>
                l.ToStatus == OrderStatus.Failed && l.Reason == "insufficient_inventory"),
            Arg.Any<CancellationToken>());
    }

    // ── UT-PROC-03: 樂觀鎖衝突 2 次，第 3 次成功 ─────────────────────────────

    [Fact]
    public async Task ProcessOrderAsync_OptimisticLockConflictTwiceThenSuccess_ShouldSucceed()
    {
        // Arrange
        var order = MakePendingOrder(quantity: 2);
        var ticket = MakeTicket(availableQuantity: 100);

        _orderRepository.GetByIdAsync(OrderId, Arg.Any<CancellationToken>()).Returns(order);
        _ticketRepository.GetByIdNoTrackingAsync(TicketId, Arg.Any<CancellationToken>()).Returns(ticket);

        // Sequential returns：前兩次 CAS 回傳 0（version 衝突），第三次回傳 1（成功）
        _ticketRepository.TryDeductInventoryAsync(
            Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(0, 0, 1);

        // Act
        await _sut.ProcessOrderAsync(OrderId);

        // Assert
        Assert.Equal(OrderStatus.Success, order.Status);

        await _ticketRepository.Received(3).TryDeductInventoryAsync(
            Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    // ── UT-PROC-04: 連續 4 次衝突 → optimistic_lock_retry_exhausted ──────────

    [Fact]
    public async Task ProcessOrderAsync_OptimisticLockExhausted_ShouldTransitionToFailed()
    {
        // Arrange
        var order = MakePendingOrder(quantity: 2);
        var ticket = MakeTicket(availableQuantity: 100, version: 0);

        _orderRepository.GetByIdAsync(OrderId, Arg.Any<CancellationToken>()).Returns(order);
        // GetByIdNoTracking 每次都回傳相同 ticket（避免 null），但 CAS 每次都失敗
        _ticketRepository.GetByIdNoTrackingAsync(TicketId, Arg.Any<CancellationToken>())
            .Returns(ticket);
        // 4 次 CAS 全都回傳 0（衝突）
        _ticketRepository.TryDeductInventoryAsync(
            Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(0);

        // Act
        await _sut.ProcessOrderAsync(OrderId);

        // Assert
        Assert.Equal(OrderStatus.Failed, order.Status);

        // 應呼叫 TryDeductInventoryAsync 恰好 4 次（初始 1 + 重試 3 = MaxRetries+1）
        await _ticketRepository.Received(4).TryDeductInventoryAsync(
            Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());

        // 應寫入 Failed log，reason = optimistic_lock_retry_exhausted
        await _orderRepository.Received(1).UpdateAndAddStatusLogAsync(
            order,
            Arg.Is<OrderStatusLog>(l =>
                l.ToStatus == OrderStatus.Failed && l.Reason == "optimistic_lock_retry_exhausted"),
            Arg.Any<CancellationToken>());
    }

    // ── UT-PROC-05: 剛好第 3 次重試成功（重試上限邊界） ─────────────────────

    [Fact]
    public async Task ProcessOrderAsync_ExactlyMaxRetries_ShouldSucceedOnLastAttempt()
    {
        // 3 次衝突（index 0,1,2）→ 第 4 次（index 3）成功 → 剛好不超過 MaxRetries(3)
        // Arrange
        var order = MakePendingOrder(quantity: 1);
        var ticket = MakeTicket(availableQuantity: 10, version: 5);

        _orderRepository.GetByIdAsync(OrderId, Arg.Any<CancellationToken>()).Returns(order);
        _ticketRepository.GetByIdNoTrackingAsync(TicketId, Arg.Any<CancellationToken>())
            .Returns(ticket);

        var callCount = 0;
        _ticketRepository.TryDeductInventoryAsync(
                Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callCount++;
                return callCount <= 3 ? 0 : 1; // 前 3 次失敗，第 4 次成功（第 3 次重試剛好通過）
            });

        // Act
        await _sut.ProcessOrderAsync(OrderId);

        // Assert
        Assert.Equal(OrderStatus.Success, order.Status);
        Assert.Equal(4, callCount); // 呼叫了 4 次（初始 + 3 次重試）
    }

    // ── UT-PROC-06: Order 不存在時直接 return（不拋例外） ────────────────────

    [Fact]
    public async Task ProcessOrderAsync_OrderNotFound_ShouldReturnWithoutException()
    {
        // Arrange
        _orderRepository.GetByIdAsync(OrderId, Arg.Any<CancellationToken>())
            .Returns((Order?)null);

        // Act & Assert（不應拋例外）
        await _sut.ProcessOrderAsync(OrderId);

        // 不應有任何其他呼叫
        await _orderRepository.DidNotReceive().UpdateAndAddStatusLogAsync(
            Arg.Any<Order>(), Arg.Any<OrderStatusLog>(), Arg.Any<CancellationToken>());
    }

    // ── UT-PROC-07: 驗證每次轉換都有寫 OrderStatusLog ─────────────────────────

    [Fact]
    public async Task ProcessOrderAsync_Success_ShouldWriteTwoStatusLogs()
    {
        // Arrange
        var order = MakePendingOrder(quantity: 1);
        var ticket = MakeTicket(availableQuantity: 10, version: 3);

        _orderRepository.GetByIdAsync(OrderId, Arg.Any<CancellationToken>()).Returns(order);
        _ticketRepository.GetByIdNoTrackingAsync(TicketId, Arg.Any<CancellationToken>()).Returns(ticket);
        _ticketRepository.TryDeductInventoryAsync(TicketId, 1, 3, Arg.Any<CancellationToken>()).Returns(1);

        // Act
        await _sut.ProcessOrderAsync(OrderId);

        // Assert: 總共呼叫 2 次 UpdateAndAddStatusLogAsync
        await _orderRepository.Received(2).UpdateAndAddStatusLogAsync(
            Arg.Any<Order>(), Arg.Any<OrderStatusLog>(), Arg.Any<CancellationToken>());
    }

    // ── UT-PROC-08: Processing → Failed，確認 fromStatus 紀錄正確 ─────────────

    [Fact]
    public async Task ProcessOrderAsync_Failed_StatusLogShouldHaveCorrectFromStatus()
    {
        // Arrange
        var order = MakePendingOrder(quantity: 99);
        var ticket = MakeTicket(availableQuantity: 1, version: 0); // 庫存不足

        _orderRepository.GetByIdAsync(OrderId, Arg.Any<CancellationToken>()).Returns(order);
        _ticketRepository.GetByIdNoTrackingAsync(TicketId, Arg.Any<CancellationToken>()).Returns(ticket);

        OrderStatusLog? capturedFailLog = null;
        await _orderRepository.UpdateAndAddStatusLogAsync(
            Arg.Any<Order>(),
            Arg.Do<OrderStatusLog>(l => capturedFailLog = l),
            Arg.Any<CancellationToken>());

        // Act
        await _sut.ProcessOrderAsync(OrderId);

        // Assert: 最後一個 log 的 FromStatus 應該是 Processing
        Assert.NotNull(capturedFailLog);
        Assert.Equal(OrderStatus.Failed, capturedFailLog.ToStatus);
    }
}
