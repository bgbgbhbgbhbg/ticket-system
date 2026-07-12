namespace TicketBooking.Api.Dtos;

/// <summary>
/// 票券回應 DTO，對應 api-spec.yaml 的 TicketResponse schema
/// </summary>
public class TicketResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string EventName { get; set; } = null!;
    public DateTime EventStartAt { get; set; }
    public decimal Price { get; set; }
    public int AvailableQuantity { get; set; }
}
