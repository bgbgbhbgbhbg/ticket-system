# Message Contracts Specification

> 定義 RabbitMQ 在本系統中的 Queue 設計與訊息 Payload 格式。任何程式碼發送/消費訊息前,先對照本文件確認格式一致。

---

## 1. Exchange / Queue 設計

```
Exchange: order.exchange (type: direct)
    │
    ├── routing key: order.created ──▶ Queue: order.processing.queue
    │                                        │
    │                                        └── 消費失敗達重試上限 ──▶ order.processing.dlq (Dead Letter Queue)
    │
    └── (預留擴充: order.cancelled, order.refunded 等未來事件)
```

| 名稱 | 型別 | 說明 |
|---|---|---|
| `order.exchange` | direct exchange | 訂單相關事件的統一入口 |
| `order.processing.queue` | queue | BackgroundService 消費,執行庫存扣減 |
| `order.processing.dlq` | queue(dead-letter) | 消費失敗達重試上限的訊息進這裡,供人工排查,不會無限重試卡住整個 queue |

---

## 2. 訊息 Payload 格式

### 2.1 `order.created`(API 建立訂單後發送)

```json
{
  "messageId": "uuid",
  "eventType": "order.created",
  "occurredAt": "2026-07-09T10:00:00Z",
  "payload": {
    "orderId": "uuid",
    "userId": "uuid",
    "ticketId": "uuid",
    "quantity": 2
  }
}
```

**欄位說明**:
- `messageId`:每則訊息獨立的 UUID,用於 log 追蹤,**不是** idempotency key(idempotency 是在 API 層用 `Idempotency-Key` header 處理,見 `docs/3_specs/api-spec.yaml`)
- `occurredAt`:事件發生時間,不是訊息被消費的時間,兩者可能因為 queue 堆積而有落差,這個落差本身就是壓測時要觀察的指標之一

---

## 3. 消費端(BackgroundService)處理邏輯

```
收到 order.created 訊息
    ↓
訂單狀態 Pending → Processing (寫 DB + order_status_logs)
    ↓
執行 domain-state-machine.md 定義的樂觀鎖重試邏輯
    ↓
成功 → Processing → Success，ack 訊息
失敗(庫存不足) → Processing → Failed，ack 訊息(業務邏輯失敗，不是技術異常，不需要重新投遞)
系統例外(DB 連線中斷等) → nack 訊息，交給 RabbitMQ 重新投遞
```

**關鍵設計原則**:業務邏輯上的失敗(庫存不足)跟技術上的失敗(DB 連不上)要分開處理——前者 ack 掉,因為重新投遞也不會讓庫存變多,只是浪費資源;後者才 nack,因為有機會在下次重試時連線恢復。

---

## 4. Dead Letter Queue 設定

```
- 訊息重新投遞達 3 次仍失敗(技術性錯誤持續發生)→ 自動轉入 order.processing.dlq
- DLQ 內的訊息不會自動處理，需要人工介入查看（對應之後可以做的 admin 監控畫面）
- 監控重點：DLQ 長度 > 0 應該觸發告警（本專案練習階段可以先用 RabbitMQ Management UI 手動觀察 http://localhost:15672）
```

---

## 5. 與其他文件的對應

- 訊息消費後的狀態轉換規則 → `docs/3_specs/domain-state-machine.md`
- 樂觀鎖重試邏輯 → `docs/3_specs/data-model.md` 第 2.2 節、`adr-003-oversell-prevention-strategy.md`
- 為什麼選 RabbitMQ 而非其他 MQ → `adr-001-why-rabbitmq-over-kafka.md`