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
    // DB column: order_status_logs.reason varchar(500)
    // Controller 會加上 "admin_manual_override: "（23 字元）前綴，
    // 因此 Reason 本身的上限為 500 - 23 = 477
    [MaxLength(477)]
    public string Reason { get; set; } = null!;
}
