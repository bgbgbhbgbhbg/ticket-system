namespace TicketBooking.Application.Interfaces.Services;

/// <summary>
/// 訂單處理服務 Interface，對應 docs/3_specs/domain-state-machine.md 與 message-contracts.md 第 3 節。
/// 實際消費 RabbitMQ 訊息的 OrderProcessingWorker 呼叫這個服務執行業務邏輯。
/// </summary>
public interface IOrderProcessingService
{
    /// <summary>
    /// 處理單筆訂單：
    /// 1. Pending → Processing（寫 DB + OrderStatusLog）
    /// 2. 執行樂觀鎖 CAS 重試迴圈扣庫存
    /// 3. 成功 → Success；庫存不足/重試耗盡 → Failed
    /// 所有業務失敗(Success/Failed)都算完成，呼叫端應 ack。
    /// 只有技術性例外才應 nack（在 Worker 層 catch 處理，不在這裡）。
    /// </summary>
    Task ProcessOrderAsync(Guid orderId, CancellationToken cancellationToken = default);
}
