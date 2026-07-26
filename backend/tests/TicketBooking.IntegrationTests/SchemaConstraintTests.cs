using Microsoft.EntityFrameworkCore;
using TicketBooking.Domain.Entities;
using TicketBooking.Infrastructure.Persistence;

namespace TicketBooking.IntegrationTests;

/// <summary>
/// 資料庫約束整合測試：驗證 CHECK constraint、UNIQUE index、total_amount 快照特性。
/// 對應 docs/test-plan.md 第 2 節整合測試：
///   - IT-SCHEMA-01: available_quantity 的 CHECK constraint 生效
///   - IT-IDEM-01:   idempotency_key 複合唯一索引生效
///   - IT-SNAP-01:   total_amount 快照特性（票價異動後舊訂單金額不變）
/// </summary>
[Collection("OptimisticLock")]
public class SchemaConstraintTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;
    private AppDbContext _dbContext = null!;

    public SchemaConstraintTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;
        _dbContext = new AppDbContext(options);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
    }

    // ── IT-SCHEMA-01: available_quantity 的 CHECK constraint 生效 ────────────

    /// <summary>
    /// 直接對資料庫送 raw SQL，嘗試把 available_quantity 設成 -1，
    /// 驗證是「資料庫層」的 CHECK constraint 擋住（不是應用層程式碼）。
    /// 對應 data-model.md 2.2 節：ck_tickets_available_quantity。
    /// </summary>
    [Fact]
    public async Task CheckConstraint_NegativeAvailableQuantity_ShouldBeRejectedByDatabase()
    {
        // Arrange：先建立一張合法 ticket
        var ticket = Ticket.Create("約束測試票", "測試活動", DateTime.UtcNow.AddDays(1), 5, 100m);
        _dbContext.Tickets.Add(ticket);
        await _dbContext.SaveChangesAsync();

        // Act & Assert：繞過應用層，直接用 raw SQL 嘗試違反 CHECK constraint
        // ExecuteSqlAsync 使用 FormattableString 自動參數化，防止 SQL injection
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await _dbContext.Database.ExecuteSqlAsync(
                $"UPDATE tickets SET available_quantity = {-1} WHERE id = {ticket.Id}");
        });
    }

    // ── IT-IDEM-01: idempotency_key 複合唯一索引生效 ─────────────────────────

    /// <summary>
    /// 同一個 user 送出兩筆相同 idempotency_key 的訂單，
    /// 第二筆應因 UNIQUE INDEX (user_id, idempotency_key) 衝突拋出 DbUpdateException。
    /// 對應 data-model.md 2.3 節：idx_orders_user_idempotency_key。
    /// </summary>
    [Fact]
    public async Task CreateOrder_DuplicateIdempotencyKey_ShouldThrowDbUpdateException()
    {
        // Arrange：建立 user 和 ticket
        var user = User.Create("idem-constraint@example.com", "hashedpwd", "Idem User", "User");
        _dbContext.Users.Add(user);
        var ticket = Ticket.Create("幂等測試票", "測試活動", DateTime.UtcNow.AddDays(7), 100, 200m);
        _dbContext.Tickets.Add(ticket);
        await _dbContext.SaveChangesAsync();

        // 第一筆訂單：正常建立，應成功
        var order1 = Order.Create(user.Id, ticket.Id, 1, 200m, "idempotency-constraint-test-01");
        _dbContext.Orders.Add(order1);
        await _dbContext.SaveChangesAsync();

        // Act：第二筆訂單用同一個 (userId, idempotencyKey)，透過獨立 DbContext 送出
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;
        using var ctx2 = new AppDbContext(options);
        var order2 = Order.Create(user.Id, ticket.Id, 2, 400m, "idempotency-constraint-test-01");
        ctx2.Orders.Add(order2);

        // Assert：EF Core 會把 PostgresException (UNIQUE VIOLATION) 包裝成 DbUpdateException
        await Assert.ThrowsAsync<DbUpdateException>(async () =>
            await ctx2.SaveChangesAsync());
    }

    // ── IT-SNAP-01: total_amount 快照特性 ─────────────────────────────────────

    /// <summary>
    /// 建立訂單後，即使票價發生異動，舊訂單的 total_amount 應保持快照時的值不變。
    /// 對應 data-model.md 2.3 節：total_amount 是 denormalized snapshot，不是即時計算欄位。
    /// </summary>
    [Fact]
    public async Task TotalAmount_ShouldRemainUnchanged_WhenTicketPriceIsUpdated()
    {
        // Arrange：建立 user、ticket（每張 500 元）、order（買 2 張 = 1000 元）
        var user = User.Create("snap-constraint@example.com", "hashedpwd", "Snap User", "User");
        _dbContext.Users.Add(user);
        var ticket = Ticket.Create("快照測試票", "快照活動", DateTime.UtcNow.AddDays(14), 100, 500m);
        _dbContext.Tickets.Add(ticket);
        await _dbContext.SaveChangesAsync();

        var order = Order.Create(user.Id, ticket.Id, 2, 1000m, "snapshot-constraint-test-01");
        _dbContext.Orders.Add(order);
        await _dbContext.SaveChangesAsync();

        var orderId = order.Id;
        var expectedTotalAmount = order.TotalAmount; // 1000m

        // Act：透過 raw SQL 把票價從 500 改成 999（模擬漲價）
        // ExecuteSqlAsync 自動參數化，不會有 SQL injection 風險
        await _dbContext.Database.ExecuteSqlAsync(
            $"UPDATE tickets SET price = {999m}, updated_at = now() WHERE id = {ticket.Id}");

        // Assert：重新從 DB 查詢訂單，total_amount 應維持建立當下的快照值 1000，不跟著漲成 1998
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;
        using var assertCtx = new AppDbContext(options);
        var reloadedOrder = await assertCtx.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == orderId);

        Assert.NotNull(reloadedOrder);
        Assert.Equal(expectedTotalAmount, reloadedOrder.TotalAmount);
    }
}
