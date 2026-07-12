using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using TicketBooking.Infrastructure.Persistence;

namespace TicketBooking.IntegrationTests;

/// <summary>
/// 整合測試用的 PostgreSQL Container 共享 Fixture。
/// 每個 xUnit Collection 只啟動一次容器並執行一次 Migration，
/// 所有同 Collection 的測試共用同一個 Container，避免每個測試都重新啟動的效能問題。
/// </summary>
public class PostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:18")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        await using var ctx = new AppDbContext(options);
        // 執行所有 EF Core migrations（建立完整 schema，包含 CHECK constraint 與 uuidv7 default）
        await ctx.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}

/// <summary>
/// xUnit Collection 定義：讓 [Collection("OptimisticLock")] 的所有測試類別
/// 共享同一個 PostgreSqlFixture（即同一個 Container）。
/// </summary>
[CollectionDefinition("OptimisticLock")]
public class OptimisticLockCollection : ICollectionFixture<PostgreSqlFixture> { }
