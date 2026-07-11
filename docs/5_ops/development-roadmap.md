# Development Roadmap

> 本文件定義接下來的功能開發順序與對應分支策略，基於 `docs/test-plan.md` 的測試案例與 `docs/specs/` 的完整規格。每個 task 完成後應回到 `test-plan.md` 勾選對應項目。

---

## 0. 開發原則

- **一個 feature 分支對應一個獨立可測試的功能單元**，避免單一分支包山包海導致 code review 困難
- **先實作功能再補測試**，但測試必須在同一個 PR 裡完成（Task 2 是例外，用於建立測試習慣）
- **測試覆蓋率要求**：Application 層 100%，Infrastructure 層整合測試至少覆蓋核心流程（樂觀鎖、狀態轉換）
- **每個 PR 都需要綠燈才能 merge**，見 `docs/5_ops/git-workflow.md` 第 3 節

---

## 1. Task 清單與開發順序

| 順序 | 分支名稱 | 做什麼 | 為什麼是這個順序 | 涵蓋規格 | 狀態 |
|---|---|---|---|---|---|
| 1 | `feature/tickets-read` | `TicketRepository` + `TicketService` + `TicketsController`，實作 `GET /tickets`、`GET /tickets/{id}`（**不加 Redis、不需要 JWT**） | 全系統複雜度最低的功能，先證明 DB → EF Core → Controller → JSON 回應這條路完全通了 | `specs/api-spec.yaml` (Tickets 區塊)、`specs/data-model.md` (tickets 表) | ⬜ |
| 2 | `feature/tickets-unit-test` | 幫 `TicketService` 補 Unit Test（NSubstitute mock `ITicketRepository`） | 第一次真的把 CI 跑起來看綠燈，建立「寫完功能就補測試」的習慣，分開一個小分支練習 PR 流程 | `test-plan.md` 第 1 節（雖然該節主要針對 Order，但此處練習測試框架設置） | ⬜ |
| 3 | `feature/auth` | 註冊 / 登入 / JWT 簽發，`AuthController` + `AuthService` + `IPasswordHasher` | Orders 需要 JWT 才能建立訂單，必須先做完認證；Admin 功能也依賴 role claim | `specs/api-spec.yaml` (Auth 區塊)、`specs/data-model.md` (users 表)、`adr/005` (RBAC) | ⬜ |
| 4 | `feature/orders-create` | `POST /orders`（建立訂單，狀態固定 `Pending`）+ idempotency key 檢查 + 發布訊息到 RabbitMQ + `GET /orders/{id}` 查詢單筆訂單 | 這一步先讓「建立訂單」這個動作能跑，還不涉及樂觀鎖與庫存扣減，消息送到 MQ 即完成 | `specs/api-spec.yaml` (Orders 區塊)、`specs/message-contracts.md`、`specs/data-model.md` (orders 表) | ⬜ |
| 5 | `feature/orders-worker` | `BackgroundService` 消費訊息、樂觀鎖 CAS 扣庫存、`Order.TransitionTo()`、寫 `OrderStatusLog`、技術失敗 nack / 業務失敗 ack / DLQ 設定 | 全系統最核心也最複雜的部分，前面地基都穩了才處理這塊 | `specs/domain-state-machine.md`、`specs/message-contracts.md`、`specs/data-model.md` (tickets.version、order_status_logs)、`adr/003` | ⬜ |
| 6 | `feature/orders-integration-test` | Testcontainers 驗證「兩個並發請求搶同一張票，只有一個成功」+ `available_quantity` 不會扣成負數 + idempotency key 測試 | 這是整個專案最重要的一個測試案例，值得獨立一個分支專心處理，不能只靠 mock | `test-plan.md` 第 2 節 | ⬜ |
| 7 | `feature/admin-rbac` | `AdminOrdersController`（`GET /admin/orders`、`PATCH /admin/orders/{id}/status`）+ `[Authorize(Roles = "Admin")]` + 權限測試 | 認證跟訂單都做完後，RBAC 才有東西可以保護 | `specs/api-spec.yaml` (Admin 區塊)、`adr/005` (API versioning vs RBAC)、`test-plan.md` 第 3 節 (Admin 權限測試) | ⬜ |
| 8 | `feature/redis-cache` | 幫 `GET /tickets/{id}/inventory` 加上 Cache-Aside，訂單處理完 invalidate，Redis 故障降級邏輯 | 這時候核心流程都通了，加 Redis 是效能優化，錦上添花 | `specs/cache-strategy.md`、`adr/002` (cache-aside vs write-through) | ⬜ |
| 9 | `feature/load-testing` | k6 腳本（baseline/normal/stress/breakpoint），對照 `load-testing-plan.md` 分級跑，記錄 cache hit rate、樂觀鎖衝突率 | 系統功能齊全後才適合做壓測，太早測沒有意義 | `ops/load-testing-plan.md`、`test-plan.md` 第 5 節 | ⬜ |
| 10 | `feature/observability` | `/health` endpoint、結構化 log（`traceId` 貫穿 API → MQ → Worker）、log level 設定 | 選配，有餘力再做，但對面試展示很加分 | `specs/api-spec.yaml` (Health 區塊)、`ops/observability.md` | ⬜ |

---

## 2. 檢視：是否有遺漏重大功能？

根據 `docs/1_PRD.md` 的核心功能清單，逐一對照：

| PRD 章節 | 功能 | 涵蓋的 Task | 備註 |
|---|---|---|---|
| 2.1 使用者系統 | 註冊/登入/JWT/取得使用者資訊 | Task 3 | ✅ 完整涵蓋 |
| 2.2 票券系統 | 查詢列表/詳情/即時庫存 | Task 1 (基礎查詢), Task 8 (Redis 庫存快取) | ✅ 完整涵蓋 |
| 2.3 訂單系統 | 建立訂單/查詢狀態/狀態流轉 | Task 4 (建立), Task 5 (狀態流轉) | ✅ 完整涵蓋（單筆訂單查詢在 Task 4） |
| 2.4 管理員系統 | 檢視所有訂單/手動介入狀態 | Task 7 | ✅ 完整涵蓋 |
| 2.5 高併發控制 | 樂觀鎖/RabbitMQ/Redis 三層防超賣 | Task 5 (MQ+樂觀鎖), Task 8 (Redis), Task 6 (並發測試) | ✅ 完整涵蓋 |
| 2.6 健康檢查與可觀測性 | `/health` endpoint、結構化 log | Task 10 | ✅ 完整涵蓋 |

### 2.1 潛在遺漏項目（經審視後判定不需要）

1. **`GET /orders` 使用者查詢自己的訂單列表**
   - **現況**：`specs/api-spec.yaml` 只有 `GET /orders/{id}`（查詢單筆訂單），沒有 `GET /orders`（列表）
   - **判定**：**不算遺漏，刻意簡化**
   - **理由**：搶票場景下，使用者通常只需要：
     1. 下單後取得訂單 ID（POST 回傳）
     2. 用訂單 ID 查詢狀態（GET /orders/{id}）
     3. 不需要「查看我的所有訂單歷史」功能（這是電商平台的需求，不是搶票系統的核心場景）
   - **若未來要補**：可在 Task 4 或 Task 7 後面插入 `feature/orders-list`（GET /orders + pagination），但目前不列入 MVP 範圍

2. **Migration 腳本**
   - **現況**：`backend/TicketBooking.Infrastructure/Migrations/` 目錄已存在但為空
   - **判定**：**不需要獨立 task**
   - **理由**：每個 task 實作 Entity 後，順手跑 `dotnet ef migrations add` 即可，不需要專門開一個分支只處理 migration

3. **前端整合**
   - **現況**：`frontend/` 目錄已存在，有 Next.js 骨架
   - **判定**：**暫不列入後端開發路線圖**
   - **理由**：前端整合是獨立的開發線，有自己的 task 清單（見 `frontend/AGENTS.md` 如果有的話），本文件專注於後端 API 開發，前後端整合測試見 `docs/前後端整合測試指南.md`

---

## 3. 分支策略（對應 `docs/5_ops/git-workflow.md`）

### 3.1 命名規則
- 功能分支：`feature/{簡短描述}`
- 修復分支：`fix/{issue-描述}`
- 熱修復：`hotfix/{critical-issue}`

### 3.2 工作流程
1. 從 `main` checkout 新分支：`git checkout -b feature/tickets-read`
2. 開發完成後 commit（遵循 Conventional Commits）：`feat(api): implement GET /tickets endpoint`
3. Push 並開 PR：`git push origin feature/tickets-read`
4. Code review + CI 綠燈後 merge 回 `main`
5. 刪除遠端分支：`git branch -d feature/tickets-read`

### 3.3 Commit Message 格式
```
{type}({scope}): {描述}

[可選] Body: 詳細說明

[可選] Refs: #issue-number, test-plan.md Line 42
```

**Type 列表**：`feat`、`fix`、`test`、`refactor`、`docs`、`chore`

**Scope 列表**：`api`、`domain`、`application`、`infra`、`db`、`ci`、`docs`

---

## 4. 各 Task 的成功標準（Definition of Done）

每個 task 完成時必須滿足：

1. **功能完整**：對應的 API endpoint 能正常回傳符合 `api-spec.yaml` 的 JSON
2. **測試通過**：
   - Task 1-8：至少有對應的 Unit Test（Application 層）
   - Task 5-6：必須有 Integration Test 驗證並發行為
   - Task 9：壓測結果記錄在 `setup/load-test-results.md`（新建）
3. **CI 綠燈**：`dotnet test` 無失敗案例
4. **符合規範**：
   - 遵循 `AGENTS.md` 的所有規則（架構分層、命名慣例、錯誤處理）
   - 回應格式符合 `api-spec.yaml`
   - 錯誤碼符合 `error-codes.md`
5. **文件更新**：
   - 回到 `test-plan.md` 勾選對應測試案例（⬜ → ✅）
   - 如果有架構決策變更，補充對應 ADR
6. **Code Review 通過**：至少一位 reviewer approve（如果是個人專案，self-review 時對照 `AGENTS.md` checklist）

---

## 5. 時程預估（參考）

基於個人開發（利用晚上/週末）：

| Task | 預估時間 | 累積天數 |
|---|---|---|
| Task 1 | 3-4 小時（含 EF Core 設定、基本 CRUD） | Day 1 |
| Task 2 | 2 小時（設置測試專案、寫前幾個 test case） | Day 2 |
| Task 3 | 4-5 小時（JWT 設定、密碼雜湊、註冊登入） | Day 3-4 |
| Task 4 | 3-4 小時（訂單建立、idempotency、RabbitMQ 發送） | Day 5 |
| Task 5 | **6-8 小時**（最複雜，樂觀鎖重試、狀態機、DLQ） | Day 6-8 |
| Task 6 | 4-5 小時（Testcontainers 設置、並發測試） | Day 9-10 |
| Task 7 | 2-3 小時（Admin endpoint + RBAC middleware） | Day 11 |
| Task 8 | 3-4 小時（Redis 整合、cache-aside、invalidate） | Day 12 |
| Task 9 | 3-4 小時（k6 腳本、分級測試、結果分析） | Day 13-14 |
| Task 10 | 2-3 小時（health endpoint、structured logging） | Day 15 |

**總計約 2-3 週**（每天投入 2-3 小時的節奏）

---

## 6. 風險與應變

| 風險 | 影響的 Task | 應變措施 |
|---|---|---|
| EF Core migration 出錯，無法建立表格 | Task 1, 3, 4 | 優先除錯 DB 連線，參考 `docs/docker問題除錯.md` |
| RabbitMQ 本機連線不穩 | Task 4, 5 | 檢查 `docker-compose.yml` 的 port mapping 與 health check |
| Testcontainers 在 M5 Mac 上跑不動 | Task 6 | 改用真實 Docker container 手動啟動，或用 EF Core In-Memory DB 降級測試（但會失去真實 SQL 驗證） |
| 樂觀鎖邏輯寫錯，整合測試失敗 | Task 5, 6 | 回到 `specs/data-model.md` 2.2 節對照 CAS SQL，加 debug log 觀察 version 欄位 |
| k6 壓測本機資源不足（CPU/Memory 炸掉） | Task 9 | 降低 `load-testing-plan.md` 的並發數，以「找出第一個瓶頸」為目標，不追求不切實際的高並發數字 |

---

## 7. 下一步

1. **立即開始**：checkout `feature/tickets-read` 分支，開始實作 Task 1
2. **建立 CI**：Task 2 完成後設置 GitHub Actions（`.github/workflows/dotnet.yml`），確保後續 PR 都自動跑測試
3. **定期回顧**：每完成 3 個 task，回來這份文件檢視進度，調整時程預估

---

## 8. 相關文件

- 規格來源：`docs/1_PRD.md`、`docs/3_specs/`
- 測試追蹤：`docs/test-plan.md`
- Git 流程：`docs/5_ops/git-workflow.md`
- 環境設置：`SETUP.md`
- AI 協作規範：`AGENTS.md`
