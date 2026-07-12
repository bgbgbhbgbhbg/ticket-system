using System.ComponentModel.DataAnnotations;

namespace TicketBooking.Api.Dtos;

public class CreateOrderRequest
{
    [Required]
    public Guid TicketId { get; set; }

    [Required]
    [Range(1, 10, ErrorMessage = "購買數量必須介於 1 到 10 之間")]
    public int Quantity { get; set; }
}
