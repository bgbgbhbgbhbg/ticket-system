using System.ComponentModel.DataAnnotations;

namespace TicketBooking.Api.Dtos;

public class CreateOrderRequest
{
    // Guid 是 value type，[Required] 無法攔截未提供欄位（會變成 Guid.Empty）
    // Guid.Empty 的情況在 Controller 顯式檢查，確保回傳 VALIDATION_ERROR 而非 TICKET_NOT_FOUND
    public Guid TicketId { get; set; }

    // 上限 10 由 OrderService 負責，才能回傳 ORDER_QUANTITY_EXCEEDS_LIMIT（而非 VALIDATION_ERROR）
    // 此處只驗證最低下限 >= 1
    [Range(1, int.MaxValue, ErrorMessage = "購買數量至少為 1")]
    public int Quantity { get; set; }
}
