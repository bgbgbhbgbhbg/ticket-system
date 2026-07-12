# Tech Debt / 暫緩事項追蹤

> 記錄「已經發現、但決定先不處理」的問題,避免討論過就忘記。有空、或影響到後續功能時再回來處理。

---

## 1. `AuthService` 直接依賴 JWT 技術套件,沒有比照 `IPasswordHasher` 抽象化

**發現時間**:Task 3(Auth 功能)review 階段

**問題**:`AuthService`(Application 層)直接 `using System.IdentityModel.Tokens.Jwt` 跟 `Microsoft.Extensions.Configuration`,自己組裝 `JwtSecurityTokenHandler` 簽發 token。這違反 Clean Architecture「Application 層只透過 interface 呼叫技術細節」的原則——跟 `PasswordHasher` 已經做對的抽象化方式(`IPasswordHasher` interface + Infrastructure 實作)不一致。

**建議修法**(先記錄,之後再做):
- 新增 `TicketBooking.Application/Interfaces/Security/IJwtTokenGenerator.cs`
- 把 `AuthService.GenerateJwtToken()` 的程式碼搬到 `TicketBooking.Infrastructure/Security/JwtTokenGenerator.cs`
- `AuthService` 改成注入 `IJwtTokenGenerator`,不再直接依賴 JWT 套件跟 `IConfiguration`
- `TicketBooking.Application.csproj` 移除 `System.IdentityModel.Tokens.Jwt`、`Microsoft.Extensions.Configuration.Abstractions` 這兩個 PackageReference
- `AuthServiceTests.cs` 把 `Substitute.For<IConfiguration>()` 換成 `Substitute.For<IJwtTokenGenerator>()`

**優先順序**:不阻塞任何功能開發,現在的寫法能正常運作,單純是架構一致性問題。建議在 Orders 功能(Task 4、5)都做完、進入穩定期後,找一個獨立的 `refactor/jwt-abstraction` 分支處理。

---

## 2. `IX_order_status_logs_order_id` 索引命名沒有照 `idx_xxx` 慣例

**發現時間**:InitialCreate migration review 階段

**問題**:EF Core 自動幫外鍵產生的索引名稱是 `IX_order_status_logs_order_id`,跟你手動命名的其他索引(`idx_orders_user_id` 等)大小寫風格不一致。

**建議修法**:在 `OrderStatusLogConfiguration.cs` 的 `HasIndex(l => l.OrderId)` 後面加 `.HasDatabaseName("idx_order_status_logs_order_id")` 明確命名。

**優先順序**:純風格問題,不影響任何功能,有空再改。