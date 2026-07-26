namespace TicketBooking.Api.Dtos;

/// <summary>
/// GET /api/v1/admin/orders 的分頁回應（對應 api-spec.yaml Admin 區塊）
/// </summary>
public class PagedOrderResponse
{
    public List<OrderResponse> Items { get; set; } = [];
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}
