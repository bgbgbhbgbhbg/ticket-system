# Ticket Booking System PRD

> 本文件為產品需求主文件。細節規格已拆分至 `docs/specs/`,決策理由拆分至 `docs/adr/`,本文件只保留「要做什麼」,不重複「為什麼這樣做」的細節論述。

---

## 1. 產品目標

建立一個「高併發票券預訂系統模擬平台」,用於展示現代後端系統設計能力,包括:

- 高併發請求處理與防超賣
- 快取機制(Redis)
- 非同步處理(Message Queue)
- API 權限分級與版本管理
- 資料一致性控制
- 系統壓力測試與瓶頸分析能力

本系統不對接真實票務平台,僅用於工程練習與面試作品展示。

---

## 2. 系統核心功能

### 2.1 使用者系統
- 註冊帳號 / 登入(JWT Authentication)
- 取得使用者資訊(含角色 `User` / `Admin`)

規格詳見:`specs/api-spec.yaml`(Auth 區塊)、`specs/data-model.md`(users 表)

---

### 2.2 票券系統
- 查詢票券列表 / 詳情
- 查詢即時剩餘庫存(走 Redis cache-aside)

規格詳見:`specs/api-spec.yaml`(Tickets 區塊)、`specs/cache-strategy.md`

---

### 2.3 訂單系統(核心)
- 使用者建立訂單(搶票)
- 查詢訂單狀態
- 訂單狀態流轉:`Pending → Processing → Success / Failed`

規格詳見:`specs/api-spec.yaml`(Orders 區塊)、`specs/domain-state-machine.md`

---

### 2.4 管理員系統(新增)
- 管理員檢視所有使用者的訂單清單,支援依狀態篩選、分頁
- 管理員可手動介入卡住的訂單狀態(仍受狀態機合法轉換規則限制)
- 與一般使用者 API 完全區隔的 endpoint(`/admin/*`),而非同一 endpoint 依角色回傳不同內容

規格詳見:`specs/api-spec.yaml`(Admin 區塊)、`adr/005-api-versioning-and-rbac.md`

**練習目的**:區分「API 版本號」與「角色權限」兩個維度——版本號解決契約相容性問題,角色權限解決存取控制問題,兩者不應混用同一套機制。

---

### 2.5 高併發控制(重點)

三層防超賣機制:

1. Redis 預檢(效能優化,非正確性保證)
2. RabbitMQ 序列化(流量整形,非正確性保證)
3. PostgreSQL 樂觀鎖(`version` 欄位 CAS,唯一正確性保證)

規格詳見:`adr/003-oversell-prevention-strategy.md`、`specs/data-model.md`(2.2 節)

---

### 2.6 系統健康檢查與可觀測性
- `/health` endpoint,區分核心依賴(DB/MQ,故障回報 Unhealthy)與輔助依賴(Redis,故障回報 Degraded)
- 結構化 log,以 `traceId` 貫穿 API → MQ → Worker 全流程
- (選配)Prometheus metrics,觀察 cache hit rate、樂觀鎖衝突率等

規格詳見:`ops/observability.md`

---

## 3. 非功能需求

### 3.1 性能目標(依開發者本機硬體調整,見下方 3.4)
- API response < 200ms(cache hit 情境)
- 訂單最終一致性(eventual consistency),經 MQ 保證非同步處理可靠性

### 3.2 可用性
- API stateless,支援水平擴展設計(理論)

### 3.3 一致性
- 訂單最終一致性,DB 樂觀鎖為唯一正確性防線(見 2.5)

### 3.4 壓力測試目標(已依硬體條件調整,不追求不切實際的高並發數字)

開發環境:MacBook Pro / Apple M5 / 24GB Unified Memory。

| 階段 | 並發數 | 性質 |
|---|---|---|
| Baseline | 50 | 本機實測 |
| Normal Load | 300 | 本機實測 |
| Stress | 1000 | 本機實測,找出第一個瓶頸 |
| Breakpoint | 1500~2000 | 本機實測,測到系統明顯降級即停止 |
| 更高並發(如萬人等級) | — | 僅做理論分析與推估,不在本機實測,避免測出的數字反映的是筆電資源限制而非架構真實瓶頸 |

規格詳見:`ops/load-testing-plan.md`

---

## 4. 技術限制

- ASP.NET Core Web API(C#)
- Next.js Frontend
- PostgreSQL / Redis / RabbitMQ
- OrbStack(本機容器環境,Apple Silicon 原生支援)
- xUnit + Testcontainers
- k6(壓測)

開發工具:VS Code、GitHub、GitHub Copilot、DBeaver

環境架設步驟詳見:`SETUP.md`

---

## 5. 成功標準(面試導向)

- 能完整解釋系統架構,並清楚區分各元件的職責分工(效能優化 vs 正確性保證)
- 能說明 cache / mq / db 的分工,以及三者故障時各自的降級行為
- 能說明 API 版本策略與角色權限設計的差異
- 能展示本機壓測結果,並解釋為何測試分級要依硬體條件調整
- 能定位 bottleneck(DB / queue / cache),並講出對應的解法方向
- 每個技術決策都有對應的 ADR 可以佐證推理過程,不是憑感覺選型

---

## 6. 未來擴展(Optional,不在本次範圍)

- Payment 模組(mock)
- Seat locking mechanism
- Distributed tracing(OpenTelemetry)
- Kubernetes deployment
- `order_status_logs` 加入 `operated_by` 欄位,記錄 Admin 操作稽核軌跡(見 `adr/005` 後果章節)

---

## 7. 文件地圖

```
docs/
├── PRD.md                          ← 本文件(做什麼)
├── ARCHITECTURE.md                  (系統怎麼組成)
├── specs/                           (精確規格,程式實作的合約)
│   ├── data-model.md
│   ├── domain-state-machine.md
│   ├── api-spec.yaml
│   ├── message-contracts.md
│   ├── cache-strategy.md
│   └── error-codes.md
├── adr/                              (為什麼這樣選,不是別的方案)
│   ├── 001-why-rabbitmq-over-kafka.md
│   ├── 002-cache-aside-vs-write-through.md
│   ├── 003-oversell-prevention-strategy.md
│   ├── 004-modular-monolith-vs-microservice.md
│   ├── 005-api-versioning-and-rbac.md
│   ├── 006-ddd-lite-vs-3tier.md
│   └── 007-clean-architecture-layering.md
├── test-plan.md                      (規格對應的測試案例追蹤)
└── ops/
    ├── load-testing-plan.md
    └── observability.md

SETUP.md                              (環境架設步驟,含機密管理)
```