namespace TicketBooking.Application.Interfaces.Caching;

/// <summary>
/// Cache 抽象介面，對應 docs/3_specs/cache-strategy.md。
/// 實作放 TicketBooking.Infrastructure/Cache/RedisCacheService.cs。
/// Application 層透過此介面使用 Cache，不直接依賴 StackExchange.Redis。
/// </summary>
public interface ICacheService
{
    /// <summary>
    /// 讀取 cache。
    /// 若 key 不存在或 Redis 故障，回傳 null（視同 cache miss，見 cache-strategy.md 第 5 節）。
    /// </summary>
    Task<string?> GetAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// 寫入 cache。
    /// 若 Redis 故障，靜默忽略（只記 log），不拋出例外。
    /// </summary>
    /// <param name="ttl">null 表示不設過期時間，靠主動 invalidate</param>
    Task SetAsync(string key, string value, TimeSpan? ttl = null, CancellationToken ct = default);

    /// <summary>
    /// 刪除 cache key（主動 invalidate）。
    /// 若 Redis 故障，靜默忽略（只記 log），不拋出例外。
    /// </summary>
    Task DeleteAsync(string key, CancellationToken ct = default);
}
