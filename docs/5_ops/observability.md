# Observability Specification

> 定義系統的 log、health check、基本 metrics 設計。目的是讓「系統出問題時查得到、講得出原因」,對應面試常問的「怎麼監控你的系統」。

---

## 1. Health Check(對應 `specs/api-spec.yaml` 的 `/health`)

```
GET /health
    ↓
並行檢查三個元件:
    - PostgreSQL: 執行 SELECT 1
    - Redis: 執行 PING
    - RabbitMQ: 檢查連線狀態
    ↓
彙總結果:
    - 全部 Healthy → 回傳 200, status = Healthy
    - Redis Unhealthy(其餘正常) → 回傳 200, status = Degraded(見 cache-strategy.md,Redis 故障不影響核心可用性)
    - PostgreSQL 或 RabbitMQ Unhealthy → 回傳 503, status = Unhealthy(這兩個是核心依賴,掛了系統無法正常運作)
```

**為什麼 Redis 掛了不算 Unhealthy,但 DB/MQ 掛了算**:對應 `cache-strategy.md` 第 5 節與 `adr-002` 的分工——Redis 只是效能優化層,DB 是資料正確性來源,MQ 是訂單處理的必經路徑,這兩者掛了系統就真的不能用了。

---

## 2. 結構化 Log(Structured Logging)

使用 ASP.NET Core 內建的 `ILogger` + Serilog,統一輸出 JSON 格式,方便之後如果要接 ELK / Grafana Loki 這類工具。

### 2.1 每筆訂單處理過程最少要記錄的 log

| 時機 | Log Level | 應包含欄位 |
|---|---|---|
| API 收到建立訂單請求 | Information | `orderId`(尚未產生時可先省略)、`userId`、`ticketId`、`quantity`、`idempotencyKey` |
| 訊息發布到 RabbitMQ | Information | `messageId`、`orderId` |
| Worker 開始消費訊息 | Information | `messageId`、`orderId`、佇列等待時間(`occurredAt` 與消費當下時間差) |
| 樂觀鎖衝突重試 | Warning | `orderId`、`ticketId`、目前重試次數 |
| 訂單轉為 Failed | Warning | `orderId`、`reason`(對應 error-codes.md) |
| 訂單轉為 Success | Information | `orderId` |
| 未預期例外 | Error | 完整 stack trace、`traceId` |

### 2.2 一定要帶的關聯欄位(Correlation)

每一筆 log 都要帶 `traceId`(見 `error-codes.md` 第 4 節),這樣同一筆訂單從 API 進來、經過 MQ、到 Worker 處理完成,可以用同一個 `traceId` 串起完整流程,這是「可觀測性」的核心——沒有這個關聯欄位,log 再多也串不起因果關係。

---

## 3. 基本 Metrics(選配,時間允許再做)

如果要進一步展示監控能力,可以加 `prometheus-net` 套件,暴露 `/metrics` endpoint,追蹤:

| Metric | 類型 | 用途 |
|---|---|---|
| `order_created_total` | Counter | 訂單建立總數 |
| `order_status_transition_total{to_status}` | Counter(依 label 分) | 各狀態的訂單數量分布 |
| `order_processing_duration_seconds` | Histogram | 從 Pending 到終態的處理時間分布 |
| `cache_hit_total` / `cache_miss_total` | Counter | 對應 `cache-strategy.md` 的 cache hit rate 觀察 |
| `optimistic_lock_retry_total` | Counter | 樂觀鎖衝突次數,異常升高代表搶票競爭激烈或系統瓶頸 |

這些 metrics 搭配 Grafana 可以做出一個簡單的 dashboard,壓測時(`ops/load-testing-plan.md`)一邊跑 k6 一邊看這個 dashboard,是面試展示時很直觀的畫面。**這部分屬於加分項,不是本專案的必要功能**,環境架好、核心流程跑通後有餘力再做。

---

## 4. 與其他文件的對應

- Health check 的降級邏輯 → `specs/cache-strategy.md` 第 5 節
- Log 需要記錄的 reason 值 → `specs/error-codes.md`
- traceId 格式 → `specs/error-codes.md` 第 4 節(W3C Trace Context)
- Metrics 觀察重點與壓測的搭配 → `ops/load-testing-plan.md` 第 5 節