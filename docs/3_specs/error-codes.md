# Error Codes Specification

> 統一定義 API 回傳的 `errorCode`(對應 `docs/3_specs/api-spec.yaml` 的 `ErrorResponse` schema)。前端可依 `errorCode` 做對應的 UI 文案顯示,不要用 `message` 文字內容做判斷(message 純粹給人看,可能會改文案)。

---

## 1. 錯誤碼格式規則

`{MODULE}_{REASON}`,全大寫、底線分隔。

---

## 2. 錯誤碼總表

### Auth 模組

| errorCode | HTTP Status | 說明 |
|---|---|---|
| `AUTH_EMAIL_ALREADY_EXISTS` | 409 | 註冊時 email 已存在 |
| `AUTH_INVALID_CREDENTIALS` | 401 | 登入帳號或密碼錯誤 |
| `AUTH_TOKEN_EXPIRED` | 401 | JWT 過期 |
| `AUTH_TOKEN_INVALID` | 401 | JWT 格式錯誤或簽章驗證失敗 |
| `AUTH_INSUFFICIENT_ROLE` | 403 | 角色權限不足(如非 Admin 呼叫 `/admin/*`) |

### Tickets 模組

| errorCode | HTTP Status | 說明 |
|---|---|---|
| `TICKET_NOT_FOUND` | 404 | 查無此票券 |

### Orders 模組(核心)

| errorCode | HTTP Status | 說明 |
|---|---|---|
| `ORDER_NOT_FOUND` | 404 | 查無此訂單,或訂單不屬於目前使用者 |
| `ORDER_QUANTITY_EXCEEDS_LIMIT` | 422 | 單筆訂單購買數量超過上限(見 api-spec.yaml,目前上限 10) |
| `ORDER_DUPLICATE_IDEMPOTENCY_KEY` | 409 | 重複的 `Idempotency-Key`,response body 為 **`OrderResponse`**（非 `ErrorResponse`），直接回傳原訂單讓前端導向訂單狀態頁 |
| `ORDER_INSUFFICIENT_INVENTORY` | — (非同步,不會是 HTTP response,而是訂單最終狀態 Failed 的 reason) | 對應 `order_status_logs.reason`,見 `docs/3_specs/domain-state-machine.md` |
| `ORDER_OPTIMISTIC_LOCK_RETRY_EXHAUSTED` | — (同上,非同步 reason) | 樂觀鎖重試達上限 |
| `ORDER_INVALID_STATUS_TRANSITION` | 422 | Admin 嘗試做不合法的狀態轉換(見 domain-state-machine.md 的不允許轉換列表) |

### System 模組

| errorCode | HTTP Status | 說明 |
|---|---|---|
| `SYSTEM_INTERNAL_ERROR` | 500 | 未預期的例外,不應該讓使用者看到內部細節,statusCode 500 一律回這個碼 |
| `SYSTEM_SERVICE_UNAVAILABLE` | 503 | 對應 `/health` 回報 Unhealthy 時的狀態 |

---

## 3. 兩種「錯誤」的區分(重要)

本系統有兩種性質不同的失敗,不要混為一談:

1. **同步 API 錯誤**:發生在 HTTP request/response 當下,有明確的 HTTP status code(如 `AUTH_INVALID_CREDENTIALS`),前端可以立即顯示錯誤訊息。
2. **非同步業務失敗**:訂單被受理後(HTTP 202)才在背景發生的失敗(如 `ORDER_INSUFFICIENT_INVENTORY`),這**不是 HTTP 錯誤**,而是訂單最終狀態變成 `Failed` 時,寫在 `order_status_logs.reason` 裡的值。前端要透過 `GET /orders/{orderId}` polling 才會看到這個結果。

被問到「你的錯誤處理怎麼設計」時,這個區分是重點——搶票這種「先受理、後處理」的非同步流程,不能只用傳統的同步 HTTP 錯誤碼思維去想。

---

## 4. ErrorResponse 範例

```json
{
  "errorCode": "ORDER_QUANTITY_EXCEEDS_LIMIT",
  "message": "單筆訂單最多購買 10 張",
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"
}
```

`traceId` 建議直接用 ASP.NET Core 內建的 `Activity.Current?.Id`(W3C Trace Context 格式),之後如果要加 distributed tracing(如 OpenTelemetry)可以直接沿用,不用重新設計欄位。