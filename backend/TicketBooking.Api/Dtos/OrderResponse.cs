namespace TicketBooking.Api.Dtos;

/// <summary>
/// 對應 docs/3_specs/api-spec.yaml 的 OrderResponse schema
/// </summary>
public class OrderResponse
{
    public Guid Id { get; set; }
    public Guid TicketId { get; set; }
    public int Quantity { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
