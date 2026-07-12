using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using TicketBooking.Application.Services;
using TicketBooking.Domain.Entities;
using TicketBooking.Domain.Enums;
using TicketBooking.Infrastructure.Persistence;
using TicketBooking.Infrastructure.Repositories;

namespace TicketBooking.IntegrationTests;

/// <summary>
/// 樂觀鎖 CAS 整合測試，使用 Testcontainers 啟動真實 PostgreSQL 18。
/// 對應 docs/test-plan.md 第 2 節整合測試案例。
/// 驗證重點：兩個並發請求只有一個成功扣庫存、available_quantity 不會扣成負數。
/// </summary>
[Collection("OptimisticLock")]
public class OptimisticLockTests : IAsyncLifetime
{
    // PostgreSQL 18（與 docker-compose.yml 一致，才有 uuidv7() 函式）
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:18")
        .Build();

    private AppDbContext _dbContext = null!;

    // ── 生命週期 ─────────────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        _dbContext = new AppDbContext(options);

        // 執行所有 EF Core migrations（建立完整的 schema，包含 CHECK constraint 與 uuidv7 default）
        await _dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    // ── 輔助：建立測試用 TicketRepository ────────────────────────────────────

    private TicketRepository CreateTicketRepository() => new TicketRepository(_dbContext);

    // ── IT-OPT-01: 兩個並發請求搶同一張 ticket，只有一個成功 ─────────────────

    [Fact]
    public async Task TryDeductInventory_ConcurrentRequests_OnlyOneSucceeds()
    {
        // Arrange：建立 1 張可售票的 ticket（available_quantity = 1）
        var ticket = Ticket.Create("VIP 區", "測試演唱會", DateTime.UtcNow.AddDays(30), 1, 500m);
        _dbContext.Tickets.Add(ticket);
        await _dbContext.SaveChangesAsync();

        // 取得資料庫產生的 Id 與 version
        var ticketId = ticket.Id;
        var version = ticket.Version; // 應為 0

        // Act：兩個 task 同時嘗試扣庫存（使用同一個 DbContext scope，模擬 race condition）
        // 使用獨立的 DbContext 避免 EF Core change tracking 干擾並發讀取
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        var task1 = Task.Run(async () =>
        {
            using var ctx = new AppDbContext(options);
            var repo = new TicketRepository(ctx);
            return await repo.TryDeductInventoryAsync(ticketId, 1, version);
        });

        var task2 = Task.Run(async () =>
        {
            using var ctx = new AppDbContext(options);
            var repo = new TicketRepository(ctx);
            return await repo.TryDeductInventoryAsync(ticketId, 1, version);
        });

        var results = await Task.WhenAll(task1, task2);

        // Assert：恰好一個成功（1），一個失敗（0）
        Assert.Equal(1, results.Sum());     // 總影響筆數應為 1
        Assert.Contains(1, results);        // 至少一個成功
        Assert.Contains(0, results);        // 至少一個失敗

        // 驗證 available_quantity 確實只扣了 1（不會扣成 -1）
        using var checkCtx = new AppDbContext(options);
        var updatedTicket = await checkCtx.Tickets
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == ticketId);

        Assert.NotNull(updatedTicket);
        Assert.Equal(0, updatedTicket.AvailableQuantity);  // 1 - 1 = 0（不是 -1）
        Assert.Equal(1, updatedTicket.Version);            // version 應該從 0 增加到 1
    }

    // ── IT-OPT-02: available_quantity 不能扣成負數 ──────────────────────────

    [Fact]
    public async Task TryDeductInventory_MultiConcurrentExceedingInventory_QuantityNeverNegative()
    {
        // Arrange：只有 2 張可售票，10 個並發搶購（每人買 1 張）
        var ticket = Ticket.Create("普通區", "大型演唱會", DateTime.UtcNow.AddDays(30), 2, 200m);
        _dbContext.Tickets.Add(ticket);
        await _dbContext.SaveChangesAsync();

        var ticketId = ticket.Id;
        var initialVersion = ticket.Version;

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        // Act：10 個並發請求，全部用 version=0（同一版本搶扣）
        var tasks = Enumerable.Range(0, 10).Select(_ => Task.Run(async () =>
        {
            using var ctx = new AppDbContext(options);
            var repo = new TicketRepository(ctx);
            return await repo.TryDeductInventoryAsync(ticketId, 1, initialVersion);
        })).ToArray();

        var results = await Task.WhenAll(tasks);

        // Assert
        var successCount = results.Count(r => r == 1);

        // 最多只能有 2 筆成功（庫存只有 2）
        Assert.True(successCount <= 2,
            $"成功筆數 {successCount} 超過庫存上限 2，表示發生超賣");

        // available_quantity 不能是負數
        using var checkCtx = new AppDbContext(options);
        var updatedTicket = await checkCtx.Tickets
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == ticketId);

        Assert.NotNull(updatedTicket);
        Assert.True(updatedTicket.AvailableQuantity >= 0,
            $"available_quantity = {updatedTicket.AvailableQuantity}，超賣發生！");
    }

    // ── IT-OPT-03: 完整流程 — ProcessOrderAsync 成功路徑 ─────────────────────

    [Fact]
    public async Task ProcessOrderAsync_WithRealDb_ShouldDeductInventoryAndWriteLogs()
    {
        // Arrange
        // 建立假 user（foreign key 需要）
        var user = User.Create("test@example.com", "hashedpwd", "Test User", "User");
        _dbContext.Users.Add(user);

        // 建立 ticket（庫存 5）
        var ticket = Ticket.Create("黃金區", "演唱會", DateTime.UtcNow.AddDays(30), 5, 300m);
        _dbContext.Tickets.Add(ticket);

        await _dbContext.SaveChangesAsync();

        // 建立 Pending 訂單
        var order = Order.Create(user.Id, ticket.Id, 2, 600m, "integ-test-key-01");
        _dbContext.Orders.Add(order);
        await _dbContext.SaveChangesAsync();

        var orderId = order.Id;

        // 建立 Service（使用獨立的 scoped context）
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        using var serviceCtx = new AppDbContext(options);
        var orderRepo = new OrderRepository(serviceCtx);
        var ticketRepo = new TicketRepository(serviceCtx);
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<OrderProcessingService>.Instance;
        var service = new OrderProcessingService(orderRepo, ticketRepo, logger);

        // Act
        await service.ProcessOrderAsync(orderId);

        // Assert：訂單狀態應為 Success
        using var assertCtx = new AppDbContext(options);
        var updatedOrder = await assertCtx.Orders.FindAsync(orderId);
        Assert.NotNull(updatedOrder);
        Assert.Equal(OrderStatus.Success, updatedOrder.Status);

        // available_quantity 應扣減 2（5 → 3）
        var updatedTicket = await assertCtx.Tickets
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == ticket.Id);
        Assert.NotNull(updatedTicket);
        Assert.Equal(3, updatedTicket.AvailableQuantity);

        // 應有 2 筆 OrderStatusLog（Pending→Processing、Processing→Success）
        var logs = await assertCtx.OrderStatusLogs
            .Where(l => l.OrderId == orderId)
            .OrderBy(l => l.CreatedAt)
            .ToListAsync();

        Assert.Equal(2, logs.Count);
        Assert.Equal(OrderStatus.Processing, logs[0].ToStatus);
        Assert.Equal("worker_picked_up", logs[0].Reason);
        Assert.Equal(OrderStatus.Success, logs[1].ToStatus);
        Assert.Equal("inventory_deducted", logs[1].Reason);
    }

    // ── IT-OPT-04: 庫存不足時 ProcessOrderAsync 應標記 Failed ────────────────

    [Fact]
    public async Task ProcessOrderAsync_InsufficientInventory_ShouldMarkFailed()
    {
        // Arrange：ticket 只剩 1 張，但訂單要買 3 張
        var user = User.Create("fail@example.com", "hashedpwd", "Fail User", "User");
        _dbContext.Users.Add(user);

        var ticket = Ticket.Create("站票區", "音樂節", DateTime.UtcNow.AddDays(10), 1, 100m);
        _dbContext.Tickets.Add(ticket);
        await _dbContext.SaveChangesAsync();

        var order = Order.Create(user.Id, ticket.Id, 3, 300m, "integ-insufficient-01");
        _dbContext.Orders.Add(order);
        await _dbContext.SaveChangesAsync();

        var orderId = order.Id;

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        using var serviceCtx = new AppDbContext(options);
        var orderRepo = new OrderRepository(serviceCtx);
        var ticketRepo = new TicketRepository(serviceCtx);
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<OrderProcessingService>.Instance;
        var service = new OrderProcessingService(orderRepo, ticketRepo, logger);

        // Act
        await service.ProcessOrderAsync(orderId);

        // Assert
        using var assertCtx = new AppDbContext(options);
        var updatedOrder = await assertCtx.Orders.FindAsync(orderId);
        Assert.NotNull(updatedOrder);
        Assert.Equal(OrderStatus.Failed, updatedOrder.Status);

        // ticket 庫存不應異動（沒有成功扣減）
        var updatedTicket = await assertCtx.Tickets
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == ticket.Id);
        Assert.NotNull(updatedTicket);
        Assert.Equal(1, updatedTicket.AvailableQuantity); // 保持 1，沒有被扣

        // 最後一筆 log 應是 Failed / insufficient_inventory
        var lastLog = await assertCtx.OrderStatusLogs
            .Where(l => l.OrderId == orderId)
            .OrderBy(l => l.CreatedAt)
            .LastOrDefaultAsync();
        Assert.NotNull(lastLog);
        Assert.Equal(OrderStatus.Failed, lastLog.ToStatus);
        Assert.Equal("insufficient_inventory", lastLog.Reason);
    }
}
