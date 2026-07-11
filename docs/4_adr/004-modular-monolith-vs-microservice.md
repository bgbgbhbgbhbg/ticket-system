# ADR 004: Modular Monolith vs Microservices

## 狀態
已採用(Modular Monolith)

## 背景
系統包含 Auth、Ticket、Order 三個業務模組。需要決定是拆成獨立微服務,還是放在同一個部署單元裡。

## 決策
採用 **Modular Monolith**:同一個 ASP.NET Core 專案,內部依模組(Auth / Ticket / Order)做資料夾與 namespace 隔離,對外仍是單一部署單元。

## 理由

| 考量點 | Modular Monolith | Microservices |
|---|---|---|
| 團隊規模 | 適合個人 / 小團隊開發,本專案是單人練習 | 適合多團隊各自獨立部署,團隊邊界對應服務邊界 |
| 部署與維運複雜度 | 一個 docker container 搞定,OrbStack 資源負擔小 | 每個服務獨立部署,需要 service discovery、獨立 DB,本機資源(24GB)吃緊 |
| 跨模組交易 | 同一個 DB transaction 內可以同時操作 Order 和 Ticket 表,保證強一致性 | 跨服務交易需要 Saga pattern 或 2PC,複雜度大幅提高 |
| 未來擴展性 | 模組邊界清楚的話,之後要拆成微服務阻力較小(這是「modular」的意義) | 一開始就要處理分散式系統的複雜度,對於還在驗證產品邏輯階段的專案是過度工程化 |

## 模組邊界設計(為未來若要拆分預留空間)

**重要更新(2026-07-10)**: 本節的模組邊界設計現已由 ADR 007 更新。內部程式碼組織改為 Clean Architecture 水平分層,具體結構見 `docs/adr/007-clean-architecture-layering.md`。

原先設想的垂直切法(Feature Module):
```
// ❌ 舊設計(已不採用)
TicketBooking.Api/
├── Modules/
│   ├── Auth/...
│   ├── Tickets/...
│   └── Orders/...
└── Shared/...
```

現在採用的水平分層(Clean Architecture):
```
// ✅ 現行設計(ADR 007)
TicketBooking.Domain/          # 零依賴 Entity/Enum
TicketBooking.Application/     # Service + Interface
TicketBooking.Infrastructure/  # DB/Redis/MQ 實作
TicketBooking.Api/             # Controller + DI 組裝
```

**為什麼改變**: 
- 本專案規模(3 個 Entity)還不需要垂直模組隔離的複雜度
- Clean Architecture 更能清楚展示 Dependency Inversion Principle
- 業界辨識度更高,對 demo 專案溝通成本更低

**模組間通訊原則仍然適用**:
即使在水平分層下,各模組(Auth/Tickets/Orders)的 Service 之間仍遵循:
- 不直接互相 reference Entity
- 透過 Service 介面溝通(定義在 `TicketBooking.Application/Interfaces/`)
- 保留未來若需拆微服務的彈性

## 後果
- 現階段所有模組共用同一個 PostgreSQL 實例與 connection pool,如果某個模組流量暴增,可能影響其他模組(monolith 常見的「noisy neighbor」問題),但對本專案規模而言可接受。
- **重要**: 內部模組邊界設計(如何組織 Auth/Tickets/Orders 的程式碼)由 ADR 007 定義,採用 Clean Architecture 水平分層而非垂直切法(Feature Module)。見 `docs/adr/007-clean-architecture-layering.md`。

## 技術討論重點
「我選 Modular Monolith 不是因為不會微服務,是因為在還沒有『多團隊獨立部署』或『某個模組需要獨立擴展』的真實需求前,微服務只會增加分散式交易與維運的複雜度。但我在模組邊界上刻意做了隔離,如果之後真的要拆,阻力會比一開始就是一坨義大利麵條式的 monolith 小很多。」