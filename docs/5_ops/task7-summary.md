# Task 7: Admin RBAC — 實作完成總結

## 📋 任務概覽
**日期**: 2026-07-26  
**分支**: `feature/admin-rbac`  
**狀態**: ✅ **完成**

---

## 1. 實作範圍

對應 `docs/3_specs/api-spec.yaml` Admin 區塊與 `docs/4_adr/005-api-versioning-and-rbac.md`：

| 層級 | 新增 / 修改 | 說明 |
|---|---|---|
| Application | `OrderNotFoundException` | Admin 查無訂單時拋出（放 Application/Exceptions/，需先查 Repository 才能判斷） |
| Application | `IOrderRepository.GetPagedAsync` | 分頁查詢簽名，支援狀態篩選 + 回傳總筆數 |
| Application | `IOrderService.GetOrdersAsync` | Admin 分頁查詢，pageSize 夾住上限 100 |
| Application | `IOrderService.UpdateOrderStatusAsync` | Admin 手動介入狀態，走 `TransitionTo()` 確保 domain 合法性 |
| Infrastructure | `OrderRepository.GetPagedAsync` | EF Core LINQ 實作：`Where` → `CountAsync` + `Skip/Take` |
| Application | `OrderService`（擴充） | 實作 `GetOrdersAsync` / `UpdateOrderStatusAsync`；pageSize clamp；`admin_manual_override:` reason 前綴 |
| Api | `AdminOrdersController` | `GET /api/v1/admin/orders`（分頁 + 狀態篩選）+ `PATCH /api/v1/admin/orders/{orderId}/status` |
| Api | `AdminUpdateStatusRequest` | DTO：`[RegularExpression("^(Success\|Failed)$")]` 限定 toStatus 只接受兩種值 |
| Api | `PagedOrderResponse` | DTO：`Items`、`Total`、`Page`、`PageSize` |
| Api | `CustomAuthorizationMiddlewareResultHandler` | 攔截 403 → 統一回傳 `AUTH_INSUFFICIENT_ROLE` ErrorResponse（非空 body） |
| Api | `Program.cs` | 註冊 `IAuthorizationMiddlewareResultHandler` singleton |
| Tests | UT-ORD-09 ~ UT-ORD-12 | Application 層 Admin 方法全路徑覆蓋（見下方） |
| Frontend | `api.ts` | 新增 `adminGetOrders` / `adminUpdateOrderStatus` / `PagedOrderResponse` |
| Frontend | `app/admin/orders/page.tsx` | Admin 訂單管理頁：分頁列表、狀態篩選下拉、手動介入 modal |
| Frontend | `app/page.tsx` | Admin 使用者顯示「管理後台」導航連結 |

---

## 2. 測試結果

```
Unit Tests  — 已通過! - 失敗: 0，通過: 37，略過: 0，總計: 37
Integration Tests — 已通過! - 失敗: 0，通過: 8，略過: 0，總計: 8
```

### 新增 Unit Tests

| 編號 | 測試描述 |
|---|---|
| UT-ORD-09 | `GetOrdersAsync` 正常分頁查詢，回傳 Items + Total |
| UT-ORD-09b | `GetOrdersAsync` pageSize=0 夾住為 1；pageSize=200 夾住為 100 |
| UT-ORD-10 | `UpdateOrderStatusAsync` 合法轉換（Processing → Success），呼叫 `UpdateAndAddStatusLogAsync` |
| UT-ORD-11 | `UpdateOrderStatusAsync` 訂單不存在 → `OrderNotFoundException` |
| UT-ORD-12 | `UpdateOrderStatusAsync` 終態不可逆（Success → Failed）→ `InvalidStatusTransitionException`，不寫 DB |

---

## 3. 核心設計決策

### 3.1 403 回應格式統一
- **問題**：ASP.NET Core 預設授權失敗回傳空 body 403，前端無法區分「未登入」與「角色不足」。
- **解決**：實作 `CustomAuthorizationMiddlewareResultHandler`，實作 `IAuthorizationMiddlewareResultHandler`，攔截 `authorizeResult.Forbidden`，注入統一 `ErrorResponse`（`errorCode: "AUTH_INSUFFICIENT_ROLE"`）。
- **注意**：401（未驗證）由 JWT middleware 處理，不經過此 handler，兩者分工明確。

### 3.2 Admin 不獨立模組，共用 OrderService
- 依 `AGENTS.md` 第 2 節規範：`AdminOrdersController` 放 `TicketBooking.Api/Controllers/`，商業邏輯擴充既有 `IOrderService`，不另建 `IAdminOrderService`。
- Admin 使用 `[Authorize(Roles = "Admin")]` 標記整個 class，一般使用者呼叫觸發 403。

### 3.3 toStatus 雙層驗證
- **Application Request DTO**：`[RegularExpression("^(Success|Failed)$")]` 在 ModelState 層擋住非法值（400 Validation Error）。
- **Controller**：`Enum.TryParse` 作為 defense-in-depth，轉換失敗回傳 422 VALIDATION_ERROR。
- 確保 Admin 不能把訂單手動設成 `Pending` 或 `Processing`。

### 3.4 Admin 介入仍走 TransitionTo()
- `UpdateOrderStatusAsync` 不直接設 `order.Status`，一律呼叫 `order.TransitionTo(toStatus, reason)`。
- 終態（Success / Failed）不可逆的規則由 Domain Entity 自身保護，Admin 操作也無法繞過。
- `reason` 前綴加 `"admin_manual_override: "` 標示來源，與 Worker 自動處理的 reason 區分。

### 3.5 分頁安全防護
- `pageSize` 使用 `Math.Clamp(pageSize, 1, 100)` 夾住上限，防止前端傳超大值打爆 DB。
- `page` 使用 `Math.Max(page, 1)` 防止負數或 0。
- Repository 層同時回傳 `Items`（當前頁資料）與 `Total`（符合條件總筆數），讓前端可計算總頁數。

---

## 4. Code Review 修正（本次套用）

### PR #6 Code Review — `OrderService.cs` TOCTOU NRE 防護

**問題**：`catch (DuplicateIdempotencyKeyException)` 區塊使用 `existing!`（null-forgiving operator）抑制 compiler 警告，但在極端並發情境（重覆 violation 來自尚未 commit 或最終 rollback 的交易），`GetByIdempotencyKeyAsync` 重查仍可能回傳 null，導致 Controller 後續 `MapToResponse` 拋出 NRE（500）。

**修正**：
```csharp
// Before
var existing = await _orderRepository.GetByIdempotencyKeyAsync(...);
return (existing!, false);

// After
var existing = await _orderRepository.GetByIdempotencyKeyAsync(...);
if (existing is null) throw;   // 重查仍為 null：rethrow，讓上層以正常錯誤路徑處理
return (existing, false);
```

---

## 5. test-plan.md 對應項目（已勾選）

| 項目 | 狀態 |
|---|---|
| 一般 User 呼叫 `/admin/orders` → `AUTH_INSUFFICIENT_ROLE` | ✅ |
| Admin 呼叫 `/admin/orders` 正常回傳分頁清單 | ✅ |
| 未帶 JWT 呼叫 `/orders` → 401 | ✅ |
| 下單數量超過 10 → `ORDER_QUANTITY_EXCEEDS_LIMIT` | ✅ |
| Admin 嘗試把 Success 訂單改成 Failed → `ORDER_INVALID_STATUS_TRANSITION` | ✅ |

---

## 6. 建議的 Commit 訊息

```
feat(api): 新增 Admin 訂單管理 endpoint（RBAC + 分頁查詢 + 狀態手動介入）

feat(web): 新增 /admin/orders 管理後台頁面（分頁列表 + 手動介入 modal）

test(application): 新增 UT-ORD-09~12，覆蓋 Admin Service 方法全路徑

fix(api): 自訂 403 回應格式，統一回傳 AUTH_INSUFFICIENT_ROLE ErrorResponse

fix(application): TOCTOU NRE 防護：DuplicateIdempotencyKeyException 重查為 null 時 rethrow
```

---

## 7. 相關文件

- `docs/3_specs/api-spec.yaml`（Admin 區塊）
- `docs/4_adr/005-api-versioning-and-rbac.md`
- `docs/3_specs/error-codes.md`（AUTH_INSUFFICIENT_ROLE、ORDER_INVALID_STATUS_TRANSITION）
- `docs/3_specs/domain-state-machine.md`（合法轉換表）
- `docs/test-plan.md`（第 3 節 API 測試）
