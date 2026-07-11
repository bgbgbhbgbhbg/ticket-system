# AGENTS.md — AI 協作規範

> 本文件定義 GitHub Copilot / 其他 AI 輔助工具在這個 repo 裡工作時必須遵守的規則。目的:確保 AI 產生的程式碼符合已經定案的規格,而不是自由發揮出跟文件矛盾的實作。

---

## 0. 核心開發流程 (Spec-Driven Development)

**所有程式碼實作必須以 `docs/` 裡的文件為唯一真實來源**。開發流程：

1. **需求變更 → 先改 `docs/1_PRD.md`**  
   需要新功能？先更新產品需求主文件，不要直接寫程式碼。

2. **細節設計 → 更新 `docs/3_specs/` 對應文件**  
   功能涉及 API？更新 `api-spec.yaml`。  
   涉及資料庫？更新 `data-model.md`。  
   涉及狀態邏輯？更新 `domain-state-machine.md`。  
   涉及快取/MQ/錯誤碼？對應更新各自的 spec 文件。

3. **重大架構決策 → 補一份 `docs/4_adr/`**
   參考既有的 ADR 檔案格式(背景/決策/理由/後果/相關文件)。
   不要自己決定改技術棧或改架構模式,先看有沒有相關 ADR。

4. **設計測試案例 → 更新 `docs/test-plan.md`**
   寫程式碼之前,把對應的測試案例列在 test-plan.md,確保規格完整。

5. **照規格寫程式碼**
   遵守本文件(AGENTS.md)第 2~7 節的所有規則。

6. **實作對應的測試**
   對照 test-plan.md 把測試案例勾選,不要漏掉任何環節。

7. **完成後 git commit**
   用 Conventional Commits 格式,commit message 參照 test-plan.md 對應的測試案例編號。

---

## 1. 最高原則

**`docs/` 底下的文件是唯一真實來源(Source of Truth)。**

任何程式碼實作,如果跟 `docs/3_specs/` 或 `docs/adr/` 的內容衝突,以文件為準。如果你(AI)覺得文件寫得不合理,**先提出來討論,不要自行決定改用別的做法**。

寫程式碼前,務必先讀過對應的規格文件:

| 要寫的功能 | 先讀 |
|---|---|
| Entity / Migration | `docs/3_specs/data-model.md` |
| 訂單狀態轉換邏輯 | `docs/3_specs/domain-state-machine.md` |
| Controller / API 回應格式 | `docs/3_specs/api-spec.yaml` |
| 錯誤處理 / errorCode | `docs/3_specs/error-codes.md` |
| Redis 相關程式碼 | `docs/3_specs/cache-strategy.md` |
| RabbitMQ 發布/消費邏輯 | `docs/3_specs/message-contracts.md` |
| 任何架構層級的選擇(要不要拆服務、要不要換 MQ) | 先看 `docs/adr/` 有沒有相關決策,不要重新發明 |

---

## 2. 架構風格規則

本專案架構有兩個獨立維度:
- **部署層級**:Modular Monolith,不拆微服務(見 `docs/adr/004-modular-monolith-vs-microservice.md`)
- **程式碼組織層級**:Clean Architecture 水平分層(見 `docs/adr/007-clean-architecture-layering.md`)

具體規則:

- **依賴方向只能由外往內**:`TicketBooking.Api` → `TicketBooking.Infrastructure` → `TicketBooking.Application` → `TicketBooking.Domain`。`TicketBooking.Domain` **不可以**引用任何其他專案或第三方套件(EF Core、Redis client 等一律不能出現在 Domain 專案的 PackageReference)。
- **EF Core 設定用 Fluent API,不用 Data Annotations**(見 `docs/3_specs/data-model.md` 第 0.1 節)。Entity 上不要加 `[Key]`、`[Required]`、`[Column]` 這類 attribute,每個 Entity 對應一個 `IEntityTypeConfiguration<T>` 類別放在 `TicketBooking.Infrastructure/Persistence/Configurations/`。
- **Entity 的 `Create()` factory method 裡,`Id` 屬性絕對不要指定 `Guid.NewGuid()`**,要保持 CLR 預設值(即不指定,或明確寫 `default`)。原因:所有表的 PK 都用資料庫端的 `uuidv7()` 產生(見 `docs/3_specs/data-model.md` 第 0 節),如果在 C# 端先塞一個 `Guid.NewGuid()`,EF Core 會判斷這個值「已經被明確指定」,直接把它寫進 INSERT 語句,完全繞過資料庫的 `uuidv7()` default,等於白白配置了但沒有生效。
- **Entity 要帶行為,不要寫成貧血模型**(見 `docs/adr/006-ddd-lite-vs-3tier.md`)。例如 `Order` Entity 應該有 `TransitionTo(OrderStatus to, string reason)` method,內部檢查 `docs/3_specs/domain-state-machine.md` 定義的合法轉換表,不合法就拋 `InvalidStatusTransitionException`。**不要**把這個檢查邏輯寫在 `TicketBooking.Application` 的 Service 裡。
- **不要引入 Aggregate Root、Domain Event、CQRS、Event Sourcing** 這類重量級 DDD 模式,除非有對應的 ADR 明確要求。
- `TicketBooking.Application` 只能透過 Interface(如 `IOrderRepository`、`ICacheService`、`IMessagePublisher`)呼叫 I/O,**不可以**直接 new 一個 EF Core DbContext 或 Redis client——實作永遠放在 `TicketBooking.Infrastructure`,由 `TicketBooking.Api` 的 DI 容器(`Program.cs`)組裝起來。
- Admin 相關功能(見 `docs/adr/005-api-versioning-and-rbac.md`)不需要獨立成模組,`AdminOrdersController.cs` 放在 `TicketBooking.Api/Controllers/`,用 `[Authorize(Roles = "Admin")]` 標記即可,商業邏輯複用或擴充 `TicketBooking.Application/Services/` 底下對應的 Service。

---

## 3. 程式碼撰寫規則

- **不要自己發明錯誤碼**,一律使用 `docs/3_specs/error-codes.md` 定義的 `errorCode`,如果需要新的錯誤碼,先提出來加進那份文件,再寫程式碼。
- **不要繞過樂觀鎖機制**直接用 `UPDATE tickets SET available_quantity = available_quantity - 1`,必須照 `docs/3_specs/data-model.md` 2.2 節的 CAS SQL 寫法(帶 `WHERE version = :expected_version`)。
- **API 回應格式**必須符合 `docs/3_specs/api-spec.yaml` 定義的 schema,不要自行增減欄位。如果發現規格有缺漏,先回報,不要自行決定加欄位。
- **命名慣例**:
  - C# 遵循標準 .NET 慣例(PascalCase for public members, camelCase for private fields with `_` 前綴)
  - 資料庫欄位用 snake_case(對應 `docs/3_specs/data-model.md` 的定義)
  - API JSON 欄位用 camelCase(對應 `docs/3_specs/api-spec.yaml` 的定義)
- **輸入驗證用 ASP.NET Core 內建的 DataAnnotations**(如 `[Required]`、`[Range]`、`[MaxLength]`),不引入 FluentValidation——現階段驗證邏輯不夠複雜,不需要額外套件(如未來規則複雜到 DataAnnotations 不夠用,先補一份 ADR 討論再引入)。
- **API 設計遵循 RESTful 規範**:
  - HTTP 方法用法:GET(查詢)/POST(建立)/PATCH(部分更新)/DELETE(刪除),不用 PUT(本專案不用完整覆蓋)
  - URL 路徑用複數名詞(`/orders`、`/tickets`),不用動詞
  - 回應格式須符合 `docs/3_specs/api-spec.yaml` 定義的 schema,含標準的 HTTP status code 和 `ErrorResponse` 結構
  - 所有 API 都必須有幂等性考慮:GET 永遠幂等;POST 可用 `Idempotency-Key` header 達到幂等;PATCH/DELETE 需檢查業務約束確保安全

---

## 4. 機密與安全規則(重要)

- **絕對不要**把任何連線字串、密碼、JWT secret key 寫死在程式碼或 commit 進 git。一律使用 `dotnet user-secrets`(本機開發)或環境變數(部署環境),見 `SETUP.md` 第 5 節。
- 如果你(AI)在協助 debug 時看到 `appsettings.Development.json` 或 `.env` 裡有真實密碼,**不要把內容貼到 commit message、PR 描述、或任何會被記錄下來的地方**。
- 產生範例程式碼時,連線字串一律用 placeholder(如 `<your-connection-string>`),不要生成看起來像真實密碼的字串。

---

## 5. 測試規則(強制)

- **Service 層(Application)必須 100% 寫 Unit Test**，見 `docs/test-plan.md` 第 1、2 節。
  - 每個 `*Service.cs` 都對應一個 `*ServiceTests.cs` 文件(放在 `TicketBooking.UnitTests/Services/`)
  - 使用 NSubstitute mock infrastructure interface(如 `IOrderRepository`、`ICacheService`)
  - 測試必須涵蓋:正常流程、所有例外情況、邊界值、並發邏輯
- **涉及並發/樂觀鎖邏輯的功能必須寫 Testcontainers 整合測試**，見 `docs/test-plan.md` 第 2 節。
  - 不能只靠 mock，必須用真實 PostgreSQL 驗證 CAS SQL 和 version check
  - 兩個並發請求同時扣庫存，只有一個成功的情境必須測
- **API endpoint 端對端測試**可用 xUnit + `Microsoft.AspNetCore.Mvc.Testing`，見 `docs/test-plan.md` 第 3 節。
- **訊息佇列消費邏輯**必須測試:成功消費、業務失敗(不重新投遞)、技術失敗(nack)、重試邏輯，見 `docs/test-plan.md` 第 4 節。
- 任何新功能之前必須先在 `docs/test-plan.md` 裡新增對應的測試案例，開發完成後勾選對應列。

---

## 6. 什麼時候該停下來問人,而不是自己決定

- 規格文件之間互相矛盾時
- 需要新增規格文件裡沒有定義的欄位、endpoint、錯誤碼時
- 需要引入新的第三方套件或改變既有技術棧時(見 `docs/1_PRD.md` 第 4 節技術限制)
- 任何會影響資料庫 schema 但沒有對應 migration 規劃的改動

---

## 7. Commit 規則

完整規範見 `docs/ops/git-workflow.md`,重點:

- Commit message 用 Conventional Commits + scope 格式:`{type}({scope}): {描述}`,例如 `feat(api): 新增 POST /orders endpoint`
- scope 對應目錄:`api`、`domain`、`application`、`infra`、`web`、`db`、`ci`、`docs`
- 每個 commit 盡量對應 `test-plan.md` 裡的一或多個測試項目,方便回溯
- 不要把 `bin/`、`obj/`、`node_modules/`、`.next/` 這些目錄的內容 commit 進去(已在 `.gitignore` 排除,見 `SETUP.md`)
- 骨架建立階段可直接 commit 到 main,第一個功能開始後一律走 `feature/*` 分支 + PR(見 `docs/ops/git-workflow.md` 第 0、1、2 節)