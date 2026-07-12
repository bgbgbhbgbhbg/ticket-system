# Test Plan

> 把每份規格對應到該寫的測試案例,確保「規格驅動」不只是寫文件,而是真的用文件去驅動測試撰寫。開發時勾選對照,避免漏測。

---

## 1.5 單元測試 — OrderService（Task 4）

| 測試案例編號 | 測試案例 | 狀態 |
|---|---|---|
| UT-ORD-01 | 正常建立訂單：Order.Create() factory method、TotalAmount = quantity × price、狀態 Pending、發送 MQ 訊息 | ✅ |
| UT-ORD-02 | Idempotency-Key 重複：回傳原訂單（IsNew = false），不呼叫 ticket repo / order create / publisher | ✅ |
| UT-ORD-03 | quantity 超過 10：拋出 OrderQuantityExceedsLimitException，不觸發任何 I/O | ✅ |
| UT-ORD-04 | ticketId 查無此票券：拋出 TicketNotFoundException | ✅ |
| UT-ORD-05 | TotalAmount 快照：quantity × ticket.Price 計算正確（多組數據） | ✅ |
| UT-ORD-06 | GetOrderByIdAsync 正常查詢屬於自己的訂單 | ✅ |
| UT-ORD-07 | GetOrderByIdAsync 訂單屬於其他使用者 → 回傳 null | ✅ |
| UT-ORD-08 | GetOrderByIdAsync 訂單不存在 → 回傳 null | ✅ |



| 測試案例 | 對應規則 | 狀態 |
|---|---|---|
| Pending → Processing 合法轉換成功 | 合法轉換表 | ✅ |
| Processing → Success 當庫存足夠 | 合法轉換表 | ✅ |
| Processing → Failed 當庫存不足 | 合法轉換表 | ✅ |
| Processing → Failed 當樂觀鎖重試超過 3 次 | 重試邏輯 | ✅ |
| Success → 任何狀態 應拋出例外(終態不可逆) | 不允許的轉換 | ✅ |
| Failed → 任何狀態 應拋出例外(終態不可逆) | 不允許的轉換 | ✅ |
| Pending → Success 跳級應拋出例外 | 不允許的轉換 | ✅ |
| 每次合法轉換都應寫入 order_status_logs 一筆紀錄 | 狀態機第 5 節 | ✅ |

---

## 2. 整合測試(Integration Test, Testcontainers)— 對應 `docs/3_specs/data-model.md`

| 測試案例 | 對應規則 | 狀態 |
|---|---|---|
| 樂觀鎖 CAS SQL:兩個並發請求同時扣庫存,只有一個成功 | data-model.md 2.2 節 | ✅ |
| `available_quantity` 不會扣成負數(CHECK constraint 生效) | data-model.md 2.2 節 | ✅ |
| 相同 `idempotency_key` 送兩次請求,只建立一筆訂單 | data-model.md 2.3 節 | ⬜ |
| `total_amount` 在票價異動後,舊訂單金額不變(快照特性) | data-model.md 2.3 節 | ⬜ |

---

## 3. API 測試(對應 `docs/3_specs/api-spec.yaml` + `docs/3_specs/error-codes.md`)

| 測試案例 | 對應 endpoint | 預期 errorCode | 狀態 |
|---|---|---|---|
| 註冊重複 email | `POST /auth/register` | `AUTH_EMAIL_ALREADY_EXISTS` | ⬜ |
| 登入密碼錯誤 | `POST /auth/login` | `AUTH_INVALID_CREDENTIALS` | ⬜ |
| 未帶 JWT 呼叫 `/orders` | `POST /orders` | 401 | ✅ |
| 一般 User 呼叫 `/admin/orders` | `GET /admin/orders` | `AUTH_INSUFFICIENT_ROLE` | ⬜ |
| Admin 呼叫 `/admin/orders` 正常回傳分頁清單 | `GET /admin/orders` | — | ⬜ |
| 下單數量超過 10 | `POST /orders` | `ORDER_QUANTITY_EXCEEDS_LIMIT` | ✅ |
| Admin 嘗試把 Success 訂單改成 Failed | `PATCH /admin/orders/{id}/status` | `ORDER_INVALID_STATUS_TRANSITION` | ⬜ |
| `/health` 在 Redis 斷線時回傳 Degraded | `GET /health` | — | ⬜ |

---

## 4. 訊息佇列測試(對應 `docs/3_specs/message-contracts.md`)

| 測試案例 | 狀態 |
|---|---|
| BackgroundService 消費 `order.created` 後正確扣減庫存 | ✅ |
| 業務失敗(庫存不足)應 ack 訊息,不重新投遞 | ✅ |
| 技術性失敗(模擬 DB 斷線)應 nack,訊息重新投遞 | ⬜ |
| 重新投遞達 3 次仍失敗,訊息進入 DLQ | ⬜ |

---

## 5. 壓力測試(對應 `docs/5_ops/load-testing-plan.md`)

| 測試階段 | 通過門檻 | 狀態 |
|---|---|---|
| Baseline(50 並發) | p95 < 200ms,無錯誤 | ⬜ |
| Normal Load(300 並發) | p95 < 300ms,cache hit rate > 80% | ⬜ |
| Stress(1000 並發) | 找出第一個瓶頸並記錄是哪個元件 | ⬜ |
| Breakpoint(1500~2000) | 記錄系統開始出現 5xx 的並發數 | ⬜ |

---

## 6. 使用方式

開發時每完成一個功能,回來這份文件把對應列打勾(⬜ → ✅),CI 流程可以之後加一個簡單 script 統計「打勾比例」當作進度指標,但本階段先手動維護即可,不用為此另外寫工具。