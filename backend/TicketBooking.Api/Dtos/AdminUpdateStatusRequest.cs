using System.ComponentModel.DataAnnotations;

namespace TicketBooking.Api.Dtos;

/// <summary>
/// PATCH /api/v1/admin/orders/{orderId}/status 的 request body
/// toStatus 限定 Success / Failed（api-spec.yaml 規格）
/// </summary>
public class AdminUpdateStatusRequest
{
    [Required]
    [RegularExpression("^(Success|Failed)$", ErrorMessage = "toStatus 只接受 Success 或 Failed")]
    public string ToStatus { get; set; } = null!;

    [Required]
    [MaxLength(500)]
    public string Reason { get; set; } = null!;
}
