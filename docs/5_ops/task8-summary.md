# Task 8: Redis Cache-Aside — 實作完成總結

## 📋 任務概覽
**日期**: 2026-07-27  
**分支**: `feature/redis-cache`  
**狀態**: ✅ **完成**

---

## 1. 實作範圍

對應 `docs/3_specs/cache-strategy.md` 與 `docs/4_adr/002-cache-aside-vs-write-through.md`：

### 後端

| 層級 | 新增 / 修改 | 說明 |
|---|---|---|
| Application | `ICacheService` 介面 | `GetAsync` / `SetAsync` / `DeleteAsync`；Application 層透過此介面與 cache 互動 |
| Application | `ITicketService.GetInventoryAsync` | 新增簽名：回傳 `(int AvailableQuantity, bool CacheHit)` |
| Application | `TicketService.GetInventoryAsync` | 實作 Cache-Aside：Redis miss → DB 查詢 → 寫回 cache；Redis 故障降級查 DB |
| Infrastructure | `RedisCacheService` | StackExchange.Redis 3.x 實作；所有 Redis 例外內部 catch，不拋往 Application（降級行為） |
| Infrastructure | `OrderProcessingWorker` | 注入 `ICacheService`；訂單處理完成後主動 `DeleteAsync($"ticket:{ticketId}:inventory")` invalidate |
| Api | `InventoryResponse` DTO | `TicketId` / `AvailableQuantity` / `CacheHit` |
| Api | `TicketsController.GetTicketInventory` | 新增 endpoint：`GET /api/v1/tickets/{id}/inventory`；404 時拋 `TicketNotFoundException` |
| Api | `Program.cs` | 註冊 `IConnectionMultiplexer` Singleton、`ICacheService` Singleton |
| Api | `appsettings.json` | 加入 `ConnectionStrings:Redis` 佔位符 |
| Tests | UT-CAC-01 ~ UT-CAC-04 | TicketService Unit Tests：cache hit / miss / 404 / 降級行為 |

### 前端

| 層級 | 新增 / 修改 | 說明 |
|---|---|---|
| Frontend | `api.ts` | 新增 `InventoryResponse` 型別、`tickets.inventory()` endpoint、`apiClient.getTicketInventory()` |
| Frontend | `TicketDetail.tsx` | 票券詳情頁自動查即時庫存、顯示 `cached` / `live` badge、手動重新整理按鈕 |

---

## 2. 測試結果

```
Backend Unit Tests — 已通過! - 失敗: 0，通過: 43，略過: 0，總計: 43
Frontend TypeScript — 無錯誤
```

### 新增 Unit Tests (TicketService)

| 編號 | 測試描述 | 預期行為 |
|---|---|---|
| UT-CAC-01 | Cache Hit | `ICacheService.GetAsync` 有值 → 直接回傳，不查 DB，`CacheHit=true` |
| UT-CAC-02 | Cache Miss | `ICacheService.GetAsync` 回傳 null → 查 DB → 寫回 cache（ttl=null），`CacheHit=false` |
| UT-CAC-03 | Ticket Not Found | cache miss 且 DB 查無結果 → 拋 `TicketNotFoundException` |
| UT-CAC-04 | Redis 故障降級 | `ICacheService.GetAsync` 拋例外 → TicketService catch 並降級查 DB → API 正常回傳 |

---

## 3. 核心設計決策

### 3.1 Cache Key 命名與 TTL 策略
- **Key 格式**（`cache-strategy.md` 第 1 節）：`ticket:{ticketId}:inventory`（庫存高頻異動）
- **TTL 策略**：庫存不設 TTL，只靠訂單處理完成後主動 `DEL`
  - **理由**：搶票尖峰若庫存 cache 還沒過期但 DB 已扣減，使用者看到過期數據會造成體驗問題
  - **替代方案**：不用 TTL 自動過期，依賴應用層主動 invalidate（詳見 3.3）

### 3.2 Redis 故障降級（`cache-strategy.md` 第 5 節）
- **設計**：Redis 連線例外不往上拋，全部在 `RedisCacheService` 內部吸收
  - `GetAsync` 拋例外 → 返 `null`（視同 cache miss）
  - `SetAsync`/`DeleteAsync` 拋例外 → 只記 log，不中斷主流程
- **目標**：Redis 故障不影響 API 可用性（只是變慢）
- **健康檢查**：Redis 異常 → `/health` 回傳 `Degraded` 而非 `Unhealthy`

### 3.3 Cache Invalidation 時機
- **觸發點**：`OrderProcessingWorker.HandleMessageAsync` 完成訂單狀態轉換（Success 或 Failed）後
- **具體位置**：`ProcessOrderAsync` 呼叫完 + DB transaction commit 後
- **實作**：非同步呼叫 `ICacheService.DeleteAsync(key)`，catch 例外不阻擋主流程
- **為何不主動 SET**：避免 worker 短時間內處理大量訂單時重複寫 cache

### 3.4 Application 層透過 Interface 使用 Cache
- `ICacheService` 放 `TicketBooking.Application/Interfaces/Caching/`
- `TicketService` 依賴 `ICacheService` 介面，不直接 using `StackExchange.Redis`
- 實作 `RedisCacheService` 放 `TicketBooking.Infrastructure/Cache/`
- 遵守 Clean Architecture 分層與依賴倒轉原則（AGENTS.md 第 2 節）

### 3.5 IConnectionMultiplexer 用 Singleton
- StackExchange.Redis `ConnectionMultiplexer` 須用 Singleton 注入（Program.cs 裡）
- 不可每次呼叫時 `new ConnectionMultiplexer`，會導致連線洩漏
- 連線池由 StackExchange.Redis 內部管理

---

## 4. 前端展示與交互

### 庫存狀態指示
- **即時庫存數字**：優先顯示 `/inventory` 回傳值，fallback 用票券詳情的 `availableQuantity`
- **Cache badge**：
  - 綠色 `cached`：庫存來自 Redis（快速回應）
  - 灰色 `live`：庫存直接從 DB（Redis miss 或故障）
- **重新整理按鈕**（↻）：使用者可手動觸發查詢，按鈕在載入中時 disabled

### 使用者流程
1. 進入票券詳情頁
2. 自動查一次 `/inventory`（快速獲得最新庫存）
3. 顯示「剩餘 X 張」+ cache 狀態 badge
4. 使用者可點重新整理按鈕主動查詢最新庫存
5. 庫存不足時「立即購票」按鈕 disabled 顯示「已售完」

---

## 5. 與 k6 壓測的對應

`inventory` 回應含 `cacheHit` 欄位，用於 Task 9 load-testing-plan.md 壓測時觀察 cache hit rate：

```
壓測指標：
- Baseline/Normal Load 應有 > 80% cache hit rate（庫存高度局部性）
- Stress 階段 cache hit rate 可能下降（更多 invalidate 發生）
```

---

## 6. 部署前檢查清單

- [ ] 設定 Redis 連線字串：`dotnet user-secrets set "ConnectionStrings:Redis" "localhost:6379"`
- [ ] Docker Compose Redis 服務已啟動：`docker compose up -d redis`
- [ ] 驗證 Redis 連線：`docker exec -it ticket-redis redis-cli PING`（回應 `PONG`）
- [ ] 後端 Unit Tests 全過：`dotnet test` ✅ 43/43
- [ ] 前端 TypeScript 編譯：`npx tsc --noEmit` ✅ 無錯誤
- [ ] 啟動前端：`npm run dev`
- [ ] 啟動後端：`dotnet run`
- [ ] 測試流程：進入票券詳情頁 → 觀察庫存 badge → 點重新整理 → 驗證 `cacheHit` 變化

---

## 7. 後續工作

### Task 9 壓力測試
- 使用 k6 腳本對 `/inventory` 進行分級壓測
- 記錄 cache hit rate、p95 延遲、庫存查詢吞吐量
- 驗證 Cache-Aside 在高並發下的效能提升

### Task 10 可觀測性（選配）
- 結構化 log：記錄 cache hit/miss/降級事件，便於監控
- `/health` 端點整合 Redis 健康檢查

---

## 8. 相關文件

- **規格**：`docs/3_specs/cache-strategy.md`、`docs/3_specs/api-spec.yaml` (inventory endpoint)
- **架構決策**：`docs/4_adr/002-cache-aside-vs-write-through.md`
- **測試計畫**：`docs/test-plan.md` (UT-CAC-01~04)
- **開發路線**：`docs/5_ops/development-roadmap.md` (Task 8)
