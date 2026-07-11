# ADR 007: 內部程式碼組織 — Clean Architecture(水平分層)而非 Feature Module(垂直分割)

## 狀態
已採用,取代先前非正式討論過的 Feature Module 資料夾建議

## 背景
`adr-004` 決定了**部署層級**採用 Modular Monolith(單一部署單元,不拆微服務)。但這不等於內部程式碼一定要按「功能模組」(Auth/Tickets/Orders 各自一份 Controllers/Application/Domain/Infrastructure)組織——這是另一個獨立的決策維度:程式碼要「按技術層切」還是「按功能切」。

## 決策
採用 **Clean Architecture(Onion Architecture)**,4 個獨立 .csproj,按技術層水平分層:

```
TicketBooking.Domain          # 零依賴,只有 Entity / Enum
TicketBooking.Application      # 依賴 Domain,定義 Interface + Service(商業邏輯)
TicketBooking.Infrastructure    # 依賴 Application + Domain,所有 I/O 實作(DB/Redis/MQ)
TicketBooking.Api               # 依賴 Application + Infrastructure,Controller/BackgroundTask/DI 組裝
```

不採用先前討論的 Feature Module 垂直切法(`Modules/Orders/{Controllers,Application,Domain,Infrastructure}`)。

## 理由

1. **規模不到需要模組隔離的程度**:本專案只有 User、Ticket、Order 三個核心 Entity,Feature Module 那種「每個模組自己一份四層資料夾」的重複結構,在這個規模下增加的是維護負擔,不是清晰度。
2. **Domain 純淨度更容易展示**:獨立成一個 `.csproj` 且不引用任何套件,是證明「Dependency Inversion Principle」最直接的方式——打開 `.csproj` 檔案看到零 PackageReference,比看資料夾結構更有說服力。
3. **業界辨識度**:Clean/Onion Architecture 是最多教學資源、業界最多人熟悉的分層方式,對 demo 專案而言,溝通成本比自創的模組邊界低。
4. **測試分層對應清楚**:`Application` 層的 Service 是單元測試的對象(mock `IOrderRepository` 等 interface),`Infrastructure` 層是整合測試(Testcontainers)的對象,這個對應在水平分層下非常直觀。

## 與既有決策的關係(重要,避免文件矛盾)

- `adr-004-modular-monolith-vs-microservice.md` 討論的是**部署單元**要不要拆微服務,結論不變(不拆)。這份 ADR 討論的是部署單元**內部怎麼組織資料夾**,是子決策,不衝突。ADR 004 中之前畫的 Feature Module 垂直切法已由本 ADR 更新為 Clean Architecture 水平分層。
- `adr-006-ddd-lite-vs-3tier.md` 討論的是 Entity **要不要帶行為**,這份決策完全不受影響——`Order.TransitionTo()` 這個設計原封不動,只是它現在放在 `TicketBooking.Domain/Entities/Order.cs`,而不是 `Modules/Orders/Domain/Order.cs`。

## 具體專案結構

```
backend/
├── TicketBooking.sln
├── TicketBooking.Domain/
│   ├── Entities/          # Order.cs(含 TransitionTo,見 adr-006), Ticket.cs, User.cs
│   └── Enums/             # OrderStatus.cs
├── TicketBooking.Application/
│   ├── Interfaces/        # IOrderRepository.cs, ICacheService.cs, IMessagePublisher.cs
│   └── Services/          # OrderService.cs, TicketService.cs, AuthService.cs
├── TicketBooking.Infrastructure/
│   ├── Persistence/       # TicketDbContext.cs
│   ├── Migrations/
│   ├── Repositories/      # OrderRepository.cs(含樂觀鎖 CAS SQL,見 data-model.md 2.2 節)
│   ├── Cache/             # RedisCacheService.cs
│   └── MessageBroker/     # RabbitMqPublisher.cs
└── TicketBooking.Api/
    ├── Controllers/        # 含 AdminOrdersController.cs(RBAC,見 adr-005)
    ├── BackgroundTasks/    # RabbitMqConsumerService.cs
    ├── Middlewares/        # ExceptionHandlingMiddleware(統一轉 error-codes.md 格式)
    └── Program.cs

tests/
├── TicketBooking.UnitTests/         # 測 Application 層 Service(mock Infrastructure interface)
└── TicketBooking.IntegrationTests/  # Testcontainers 測 Infrastructure 層(見 test-plan.md)
```

**Admin 相關程式碼**(對應 `adr-005`)不再獨立成一個模組,而是 `AdminOrdersController.cs` 放在 `TicketBooking.Api/Controllers/` 底下,用 `[Authorize(Roles = "Admin")]` attribute 區分,商業邏輯可以直接複用 `TicketBooking.Application/Services/OrderService.cs`(如查詢邏輯)或另外加 `AdminOrderService.cs`(如需要不同的查詢/篩選邏輯)。

## 後果

- 之前 `README.md` 和 `ARCHITECTURE.md` 裡畫的 `Modules/Auth`、`Modules/Tickets`、`Modules/Orders`、`Modules/Admin` 資料夾結構需要更新為本文件的結構。
- `AGENTS.md` 第 2 節提到的模組邊界規則也需要對應調整(改成「層與層之間的依賴方向規則」而非「模組之間不可互相引用 Entity」)。
- 如果未來專案規模真的成長到需要按功能模組化(例如加入更多完全不相關的業務領域),屆時可以在現有的 `Application/Services` 底下用資料夾分組(如 `Services/Orders/`、`Services/Payments/`)先做邏輯分組,不急著在現階段拆專案。

## 技術討論重點
「部署層級我用 Modular Monolith(不拆微服務),但內部程式碼組織我選 Clean Architecture 水平分層,因為現階段的領域複雜度(3 個 Entity)還沒到需要模組隔離的規模,水平分層更能清楚展示 Dependency Inversion——這是兩個獨立的架構決策維度,不要混為一談。」