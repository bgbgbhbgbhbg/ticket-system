namespace TicketBooking.Api.Dtos;

/// <summary>
/// GET /tickets/{id}/inventory 回應格式。
/// 對應 docs/3_specs/api-spec.yaml Tickets 區塊 inventory endpoint。
/// </summary>
public class InventoryResponse
{
    public Guid TicketId { get; set; }
    public int AvailableQuantity { get; set; }

    /// <summary>
    /// 此次資料是否來自 Redis cache。
    /// 用於 k6 壓測時觀察 cache hit rate，對應 cache-strategy.md 第 6 節。
    /// </summary>
    public bool CacheHit { get; set; }
}
