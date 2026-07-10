# ADR 006: DDD-Lite vs 純 3-Tier Architecture

## 狀態
已採用(DDD-Lite)

## 背景
在 Modular Monolith(`adr/004`)的架構下,每個模組(尤其 Orders)內部要用什麼分層風格:純 3-Tier(Controller/Service/Repository,Entity 只是資料袋子),還是導入 DDD 戰術模式(Entity 帶行為)。

## 決策
採用 **DDD-Lite**:Entity 封裝行為與不變條件(invariant),但不引入完整 DDD 的重量級模式(Aggregate Root、Domain Event、CQRS、Event Sourcing)。

## 理由

### 為什麼不用純 3-Tier(貧血模型)
`domain-state-machine.md` 定義了嚴格的狀態轉換規則(`Success`/`Failed` 為終態不可逆、不可跳級等)。如果 `Order` Entity 只是資料袋子,這些規則就得寫在 Service 層的 if-else 裡,容易出現:
- 多個 Service method 各自重複寫一次檢查邏輯,容易漏掉某個路徑
- 沒有東西「強制」呼叫端一定要檢查規則,容易被繞過(例如直接用 EF Core 改 `order.Status = "Success"` 而跳過檢查)

### 為什麼不用完整 DDD
- 本專案只有三個核心 Entity(User、Ticket、Order),領域複雜度不到需要 Aggregate Root 管理複雜物件圖的程度
- 單人開發、時間有限,CQRS/Event Sourcing 這類模式的維護成本(要維護讀寫兩套模型、事件版本相容性)在這個規模下不划算
- 面試展示的重點是「解決貧血模型問題」與「封裝領域規則」,不需要用最重的工具才能證明理解 DDD

## 具體做法

```csharp
// Domain/Order.cs
public class Order
{
    public OrderStatus Status { get; private set; }

    public void TransitionTo(OrderStatus to, string reason)
    {
        if (!IsValidTransition(Status, to))
            throw new InvalidStatusTransitionException(Status, to);

        Status = to;
        // 同時產生對應的 OrderStatusLog,由呼叫端(Application Service)負責持久化
    }

    private static bool IsValidTransition(OrderStatus from, OrderStatus to)
    {
        // 對照 domain-state-machine.md 的合法轉換表
        return (from, to) switch
        {
            (OrderStatus.Pending, OrderStatus.Processing) => true,
            (OrderStatus.Processing, OrderStatus.Success) => true,
            (OrderStatus.Processing, OrderStatus.Failed) => true,
            (OrderStatus.Processing, OrderStatus.Pending) => true, // MQ 重新投遞
            _ => false
        };
    }
}
```

Service 層(Application 層)只負責協調:呼叫 `order.TransitionTo(...)`、存 DB、發布/消費 MQ 訊息,**不重複寫轉換規則判斷**。

## 後果
- 這個決策只影響 Domain 層的內部設計,不影響 API 對外契約(`api-spec.yaml` 完全不受影響),也不影響資料庫 schema(`data-model.md` 不變)。
- 如果未來領域複雜度真的提高(例如加入座位選擇、多商品組合訂單),屆時可以在既有的 Entity 基礎上逐步導入更多 DDD 戰術模式,不需要重寫。

## 面試話術
「我用 Entity 封裝行為來解決貧血模型的問題,但沒有上到完整 DDD——這個規模的專案,Aggregate Root 或 Event Sourcing 反而是過度設計。我會依領域複雜度決定要用到哪個程度的 DDD,而不是一律套用同一套模式。」

## 相關文件
- 對應規則見 `docs/specs/domain-state-machine.md`
- 對應 AI 協作規則見 `AGENTS.md` 第 2 節