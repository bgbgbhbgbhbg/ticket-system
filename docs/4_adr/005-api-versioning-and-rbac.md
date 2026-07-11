# ADR 005: API Versioning Strategy & Role-Based Access Control

## 狀態
已採用

## 背景
新增管理員檢視清單功能後,需要練習兩件事:(1) API 版本號怎麼管理 (2) 使用者 API 與管理端 API 怎麼做權限區隔。這是原本 PRD 沒有的需求,後續追加。

---

## 決策 A:版本策略採用 URL Path Versioning

## 候選方案比較

| 方案 | 做法 | 優點 | 缺點 |
|---|---|---|---|
| **URL Path**(採用) | `/api/v1/orders` | 直觀,client 一眼看出版本,方便在 gateway/nginx 層做路由分流 | URL 會變動,舊版本要維護額外 route |
| Header Versioning | `Accept: application/vnd.ticketbooking.v1+json` | URL 乾淨不變 | 不直觀,測試/debug 時不容易一眼看出呼叫的是哪個版本 |
| Query Parameter | `/api/orders?version=1` | 實作簡單 | 容易被忽略(忘記帶 query 就退回不明確的預設行為),語意上version 不該是「參數」 |

**選 URL Path 的理由**:對於練習型專案而言,URL Path 版本號是業界最常見、最容易在 Swagger UI 上直接看出差異的做法,溝通成本最低。

## 版本策略細節
- 目前只有 `v1`,User 端與 Admin 端**共用同一個版本號**,不因為角色不同就拆版本。
- 版本號代表的是「API 契約(request/response schema)的相容性」,不是「權限層級」——這兩件事分開處理(角色權限見下方決策 B),避免版本號語意混亂。
- 未來若要出 `v2`(例如 Order 回應格式大改版),舊版 `v1` 至少保留一個 deprecation 週期(建議 3 個月),兩版並存,並在 response header 加 `Deprecation: true` 提示。

---

## 決策 B:角色權限採用 JWT Role Claim + Endpoint 層級檢查

## 做法
1. `users.role` 欄位(`User` | `Admin`,見 `specs/data-model.md`)在登入時寫入 JWT payload 的 role claim。
2. 後端用 ASP.NET Core 的 `[Authorize(Roles = "Admin")]` attribute 標記 `/admin/*` endpoint,框架層級直接擋,不需要每個 controller method 手動寫 if 判斷。
3. **不做「同一個 endpoint、依角色回傳不同欄位」這種設計**——刻意讓 User 端與 Admin 端是完全不同的 endpoint(`/orders/{id}` vs `/admin/orders`),理由:
   - 職責單一:一個 endpoint 只服務一種角色的需求,回應 schema 固定,不會有「這個欄位只有 Admin 看得到」這種條件式邏輯散落在 code 裡
   - 練習目的更清楚:題目就是要練「區分 API 權限」,拆開寫比較看得出設計意圖

## 為什麼不用更複雜的權限框架(如 Policy-Based Authorization、OAuth Scopes)
現階段只有兩種角色(User / Admin),`[Authorize(Roles=...)]` 已經足夠。如果未來角色權限矩陣變複雜(例如「Admin 可以看但不能改」「Moderator 只能改狀態不能刪除」這種細粒度需求),才需要升級成 ASP.NET Core 的 Policy-Based Authorization,現在直接上這麼重的機制屬於過度設計。

## 後果
- 目前 admin 相關 endpoint 沒有做「操作紀錄稽核」(誰在什麼時候把哪筆訂單狀態改成什麼),如果要更完整,可以之後在 `order_status_logs` 表加一個 `operated_by` 欄位記錄是哪個 admin 帳號觸發的轉換。這個先記錄下來,不在本次範圍內,避免文件範圍無限擴大。

## 技術討論重點
「版本號解決的是『契約相容性』問題,角色權限解決的是『誰能存取什麼』問題,這是兩個維度,不要用版本號去表達權限(例如不會做 `/api/v1/orders` 給 User、`/api/v1-admin/orders` 給 Admin 這種設計),分開處理邏輯才乾淨。」