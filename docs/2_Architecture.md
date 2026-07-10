# System Architecture

> 本文件描述系統的元件組成與資料流。各元件內部的精確行為定義在 `docs/specs/`,選型理由在 `docs/adr/`,本文件是連接兩者的整體視圖。

---

## 1. 整體架構

系統在**部署層級**採用 Modular Monolith(單一部署單元,不拆微服務,選型理由見 `docs/adr/004-modular-monolith-vs-microservice.md`),在**程式碼組織層級**採用 Clean Architecture 水平分層(選型理由見 `docs/adr/007-clean-architecture-layering.md`),兩者是不同維度的獨立決策。

核心目標:
- 保持簡單可開發(單人練習專案,本機資源有限)
- 模擬高併發系統設計的完整思考鏈路
- Domain 層零依賴,清楚展示 Dependency Inversion Principle

---

## 2. 架構圖(邏輯)

```
User
  ↓
Frontend (Next.js)
  ↓
Backend API (ASP.NET Core, /api/v1)
  ↓
──────────────────────────────────────
| Application Layer (Modular)        |
| - Auth Module    (User / Admin)    |
| - Ticket Module                     |
| - Order Module                      |
| - Admin Module    (RBAC 隔離)       |
──────────────────────────────────────
  ↓
Infrastructure Layer
  ↓
PostgreSQL (Source of Truth) ── Redis (Cache) ── RabbitMQ (Async Queue)
```

---

## 3. 系統元件說明

### 3.1 Backend API

- ASP.NET Core Web API,stateless design
- 統一走 `/api/v1` 版本前綴(見 `docs/adr/005-api-versioning-and-rbac.md`)
- 使用者端(Auth/Tickets/Orders)與管理端(Admin)共用同一版本號,以 JWT role claim 區分權限,而非拆版本
- 內部分四層(見 `docs/adr/007-clean-architecture-layering.md`):`TicketBooking.Domain`(零依賴)→ `TicketBooking.Application`(Interface + Service)→ `TicketBooking.Infrastructure`(DB/Redis/MQ 實作)→ `TicketBooking.Api`(Controller/BackgroundTask),依賴方向由外往內指向 Domain

---

### 3.2 PostgreSQL(資料庫)

- 唯一真實資料來源(Source of Truth)
- 儲存:Users(含 role)、Tickets(含 version 樂觀鎖欄位)、Orders、Order Status Logs
- 使用 EF Core ORM

完整欄位定義:`specs/data-model.md`

---

### 3.3 Redis(快取層)

用途:降低票券查詢延遲、減少 DB 讀取壓力。

策略:Cache-Aside(選型理由見 `docs/adr/002-cache-aside-vs-write-through.md`)

```
API → Redis → Miss → DB → Update Redis
```

**角色定位**:Redis 只負責效能優化,**不是資料正確性的來源**。故障時系統降級為直接查 DB,`/health` 回報 Degraded 而非 Unhealthy。

完整 key 設計、TTL、失效規則:`specs/cache-strategy.md`

---

### 3.4 RabbitMQ(訊息佇列)

用途:處理搶票訂單的非同步流程,削峰填谷。

```
User Request
  ↓
API creates order (Pending) + 發布訊息到 order.exchange
  ↓
order.processing.queue
  ↓
BackgroundService 消費
  ↓
處理失敗達重試上限 → order.processing.dlq
```

**角色定位**:MQ 負責流量整形,**不是正確性保證**,真正防超賣靠下一層的 DB 樂觀鎖。

完整 Exchange/Queue 設計、Payload 格式:`specs/message-contracts.md`

---

### 3.5 BackgroundService

- .NET 內建 worker,消費 RabbitMQ 訊息
- 執行訂單狀態轉換(依 `specs/domain-state-machine.md` 定義的合法轉換規則)
- 執行庫存扣減的樂觀鎖 CAS 邏輯與重試(上限 3 次)
- 完成後主動 invalidate 對應的 Redis 庫存 key

---

## 4. 核心流程設計(搶票)

```
Step 1: User sends request → POST /api/v1/orders (帶 Idempotency-Key header)
Step 2: API 檢查 idempotency_key 是否重複 → 重複則回傳原訂單
Step 3: 建立訂單(Pending)→ 寫 DB
Step 4: 發布 order.created 訊息到 RabbitMQ
Step 5: BackgroundService 消費訊息 → Pending → Processing
Step 6: 執行庫存樂觀鎖 CAS(可能重試)
Step 7: 成功 → Processing → Success;失敗 → Processing → Failed
Step 8: invalidate Redis 庫存 cache
Step 9: 前端透過 GET /api/v1/orders/{orderId} polling 取得最終結果
```

---

## 5. 高併發設計:防超賣

三層防禦(完整理由見 `docs/adr/003-oversell-prevention-strategy.md`):

```
Layer 1: Redis 預檢        — 效能優化
Layer 2: RabbitMQ 序列化   — 流量整形
Layer 3: DB 樂觀鎖(CAS)   — 唯一正確性保證
```

---

## 6. 權限與版本設計(新增模組)

```
JWT payload 含 role claim (User | Admin)
  ↓
[Authorize(Roles = "Admin")] 標記所有 /admin/* endpoint
  ↓
一般使用者 endpoint 與管理端 endpoint 完全分離
  (GET /orders/{id} 只回自己的訂單;GET /admin/orders 回所有人的訂單)
```

版本號(`/api/v1`)與角色權限是兩個獨立維度,不互相耦合。完整設計:`docs/adr/005-api-versioning-and-rbac.md`

---

## 7. Deployment Architecture(Docker / OrbStack)

Services:`frontend`(Next.js)、`backend`(ASP.NET Core)、`postgres`、`redis`、`rabbitmq`

```bash
docker compose up -d
```

完整步驟(含機密管理、`.gitignore`、`dotnet user-secrets`):`SETUP.md`

---

## 8. Testing Strategy

| 測試類型 | 工具 | 對應規格 |
|---|---|---|
| Unit Test | xUnit | `specs/domain-state-machine.md` |
| Integration Test | Testcontainers(PostgreSQL/RabbitMQ) | `specs/data-model.md` |
| API Test | xUnit + `Microsoft.AspNetCore.Mvc.Testing` | `specs/api-spec.yaml` + `specs/error-codes.md` |
| Load Test | k6 | `ops/load-testing-plan.md` |

測試案例與規格的對照追蹤表:`test-plan.md`

---

## 9. 可觀測性

- `/health`:區分核心依賴(DB/MQ)與輔助依賴(Redis)的故障回報等級
- 結構化 log,`traceId` 貫穿 API → MQ → Worker
- (選配)Prometheus metrics:cache hit rate、樂觀鎖衝突率、訂單處理耗時分布

完整設計:`ops/observability.md`

---

## 10. Bottleneck 預期分析

| Component | Bottleneck Risk | 監控依據 |
|---|---|---|
| PostgreSQL | High(write contention,樂觀鎖衝突) | `optimistic_lock_retry_total` metric |
| Redis | Medium | cache hit/miss rate |
| RabbitMQ | Medium(queue 堆積) | queue 長度、`order_processing_duration_seconds` |
| API | Low(stateless) | p95/p99 latency |

壓測分級與判讀對照:`ops/load-testing-plan.md` 第 5 節

---

## 11. 架構特點總結

- Modular Monolith 部署(易維護,單一部署單元)+ Clean Architecture 內部分層(Domain 零依賴)
- 事件驅動(RabbitMQ)+ 三層防超賣設計
- 快取加速(Redis,Cache-Aside)
- API 版本與角色權限分離設計
- DDD-Lite:Entity 封裝行為,避免貧血模型
- 可壓測(k6,依硬體條件分級)
- 可觀測(health check 分級、結構化 log、traceId 關聯)
- 每個關鍵決策都有 ADR 佐證,不是隨意選型