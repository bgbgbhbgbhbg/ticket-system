# Ticket Booking System

高併發票券預訂系統模擬平台——用於練習與展示現代後端系統設計能力(高併發控制、快取、非同步處理、防超賣、API 權限分級)的工程作品。

不對接真實票務平台,純工程練習用途。

---

## 技術棧

| 分類 | 技術 |
|---|---|
| 後端 | ASP.NET Core (C#) |
| 前端 | Next.js |
| 資料庫 | PostgreSQL |
| 快取 | Redis |
| 訊息佇列 | RabbitMQ |
| 本機容器環境 | OrbStack |
| 測試 | xUnit + Testcontainers |
| 壓測 | k6 |

開發硬體:MacBook Pro / Apple M5 / 24GB Unified Memory(壓測分級已依此調整,見 `docs/ops/load-testing-plan.md`)

---

## 核心設計亮點

- **三層防超賣**:Redis 預檢(效能)+ RabbitMQ 序列化(流量整形)+ PostgreSQL 樂觀鎖(正確性保證),三層分工明確,詳見 `docs/adr/003-oversell-prevention-strategy.md`
- **DDD-Lite**:Entity 封裝狀態轉換邏輯,避免貧血模型,詳見 `docs/adr/006-ddd-lite-vs-3tier.md`
- **Clean Architecture(水平分層)**:Domain/Application/Infrastructure/Api 四個獨立專案,Domain 零依賴,清楚展示 Dependency Inversion,詳見 `docs/adr/007-clean-architecture-layering.md`
- **API 版本號與角色權限分離設計**:版本號解決契約相容性,角色權限解決存取控制,兩者不互相耦合,詳見 `docs/adr/005-api-versioning-and-rbac.md`
- **規格驅動開發**:所有實作都對照 `docs/specs/` 的規格文件進行,而不是憑感覺邊寫邊想

---

## 快速開始

完整環境架設步驟(含 Docker Compose、機密管理、Migration)請見 [`SETUP.md`](./SETUP.md)。

簡要流程:

```bash
# 1. 啟動基礎設施(Postgres / Redis / RabbitMQ)
docker compose up -d

# 2. 套用資料庫 migration
cd backend/TicketBooking.Api
dotnet ef database update

# 3. 啟動後端(含 Swagger UI: http://localhost:5000/swagger)
dotnet watch run

# 4. 啟動前端(另開終端機)
cd frontend
npm run dev
```

---

## 文件導覽

```
docs/
├── PRD.md                 # 產品需求(做什麼)
├── ARCHITECTURE.md         # 系統架構(怎麼組成)
├── specs/                  # 精確規格(程式實作的合約)
│   ├── data-model.md
│   ├── domain-state-machine.md
│   ├── api-spec.yaml
│   ├── message-contracts.md
│   ├── cache-strategy.md
│   └── error-codes.md
├── adr/                    # 架構決策紀錄(為什麼這樣選)
│   ├── 001-why-rabbitmq-over-kafka.md
│   ├── 002-cache-aside-vs-write-through.md
│   ├── 003-oversell-prevention-strategy.md
│   ├── 004-modular-monolith-vs-microservice.md
│   ├── 005-api-versioning-and-rbac.md
│   ├── 006-ddd-lite-vs-3tier.md
│   └── 007-clean-architecture-layering.md
├── test-plan.md            # 規格對應的測試案例追蹤
└── ops/
    ├── load-testing-plan.md
    └── observability.md

AGENTS.md                   # AI 協作規範(Copilot 等工具需遵守)
SETUP.md                    # 環境架設步驟
```

---

## 開發流程(規格驅動)

```
1. 需求變更 → 先改 docs/PRD.md
2. 細節設計 → 更新 docs/specs/ 對應文件
3. 重大決策 → 補一份 docs/adr/
4. 照規格寫程式碼(AI 協作時參照 AGENTS.md)
5. 對照 docs/test-plan.md 補測試
6. k6 壓測 → 對照 docs/ops/load-testing-plan.md 的分級標準
```

---

## License

僅供個人練習與作品展示使用。