using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TicketBooking.Application.Interfaces.Caching;

namespace TicketBooking.Infrastructure.Cache;

/// <summary>
/// Redis 實作 ICacheService。
/// 對應 docs/3_specs/cache-strategy.md 第 5 節降級行為：
/// 所有 Redis 例外都在此處吸收，不往上拋到 Application 層。
/// IConnectionMultiplexer 由 Program.cs 以 Singleton 注入，不每次新建連線。
/// </summary>
public class RedisCacheService : ICacheService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisCacheService> _logger;

    public RedisCacheService(IConnectionMultiplexer redis, ILogger<RedisCacheService> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var value = await db.StringGetAsync(key);
            return value.HasValue ? (string?)value : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis GET failed for key {Key}, treating as cache miss", key);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task SetAsync(string key, string value, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            await db.StringSetAsync(key, value, ttl, When.Always);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis SET failed for key {Key}, ignoring", key);
        }
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            await db.KeyDeleteAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis DEL failed for key {Key}, ignoring", key);
        }
    }
}
