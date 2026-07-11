using TicketBooking.Domain.Enums;
using TicketBooking.Domain.Exceptions;

namespace TicketBooking.Domain.Entities;

public class Order
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid TicketId { get; private set; }
    public int Quantity { get; private set; }
    public decimal TotalAmount { get; private set; }
    public OrderStatus Status { get; private set; }
    public string IdempotencyKey { get; private set; } = null!;
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    // EF Core materialize 資料庫紀錄時需要一個建構子,設成 private 讓外部不能亂 new 出空殼物件
    // (EF Core 9 支援用非 public 建構子做物化,不需要因為這個就把它打開)
    private Order() { }

    /// <summary>
    /// 唯一合法的建立訂單方式,對應 docs/3_specs/api-spec.yaml 的 POST /orders。
    /// 建立時狀態固定是 Pending,不接受外部指定其他初始狀態。
    /// </summary>
    public static Order Create(Guid userId, Guid ticketId, int quantity, decimal totalAmount, string idempotencyKey)
    {
        var now = DateTime.UtcNow;
        return new Order
        {
            Id = Guid.NewGuid(),   // 實際存入 DB 時會被 uuidv7() 的 column default 覆蓋,這裡給值只是讓記憶體中的物件先有 Id 可用
            UserId = userId,
            TicketId = ticketId,
            Quantity = quantity,
            TotalAmount = totalAmount,
            Status = OrderStatus.Pending,
            IdempotencyKey = idempotencyKey,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    /// <summary>
    /// 狀態轉換的唯一入口,合法規則對照 docs/specs/domain-state-machine.md 第 3 節。
    /// 不合法的轉換直接拋例外,呼叫端(Application 層)負責 catch 並轉換成對應的 errorCode。
    /// </summary>
    public void TransitionTo(OrderStatus to, string reason)
    {
        if (!IsValidTransition(Status, to))
        {
            throw new InvalidStatusTransitionException(Status, to);
        }

        Status = to;
        UpdatedAt = DateTime.UtcNow;

        // 呼叫端(Application 層的 OrderService)在同一個 transaction 裡,
        // 再另外建立一筆 OrderStatusLog(fromStatus, to, reason)寫入 DB,
        // Order Entity 本身不負責寫 log,只負責狀態合法性檢查,避免 Domain 層碰 DB 相關細節。
        _ = reason; // reason 交給呼叫端拿去寫 OrderStatusLog,Entity 內部這裡不需要用到它
    }

    private static bool IsValidTransition(OrderStatus from, OrderStatus to)
    {
        // 對照 docs/specs/domain-state-machine.md 第 3 節的合法轉換表
        return (from, to) switch
        {
            (OrderStatus.Pending, OrderStatus.Processing) => true,
            (OrderStatus.Processing, OrderStatus.Success) => true,
            (OrderStatus.Processing, OrderStatus.Failed) => true,
            (OrderStatus.Processing, OrderStatus.Pending) => true, // MQ 訊息 nack、重新投遞
            _ => false
        };
    }
}