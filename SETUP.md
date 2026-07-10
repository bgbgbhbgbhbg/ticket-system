# Local Setup & Runbook

> 目標:照著這份文件從零開始,把整個系統(前端 + 後端 + DB + Redis + MQ)在你的 MacBook Pro(M5 / 24GB)上跑起來,並且清楚知道哪些東西**絕對不能 commit 進 git**。

> 💡 **指令快速查詢**: 本文件著重概念與配置邏輯,所有具體的 terminal 指令已彙集到 [commands-runbook.md](commands-runbook.md),方便複製貼上執行。

---

## 0. 前置需求安裝

你已經有 .NET 9 SDK、nvm(含最新 Node)、VS Code / GitHub / Copilot / OrbStack / DBeaver,只差 k6:

```bash
# 確認既有工具版本(應該都已經裝好)
dotnet --version   # 應顯示 9.x
node --version

# k6(壓測工具,前面 load-testing-plan.md 有用到)
brew install k6
k6 version
```

VS Code 建議額外裝的 extension:
- C# Dev Kit(官方,Microsoft)
- ESLint / Prettier(前端)
- Docker(OrbStack 相容)
- REST Client 或 Thunder Client(測 API 用,不用另外裝 Postman)

---

## 1. 專案目錄結構(建議)

```
ticket-booking-system/
├── backend/                # ASP.NET Core 專案
├── frontend/                # Next.js 專案
├── docs/                     # 前面產出的所有規格文件
│   ├── PRD.md
│   ├── ARCHITECTURE.md
│   └── specs/
├── load-tests/               # k6 腳本
├── docker-compose.yml
├── .gitignore
└── README.md
```

---

## 2. 建立後端專案(Clean Architecture,對應 `docs/adr/007-clean-architecture-layering.md`)

```bash
mkdir backend && cd backend

dotnet new sln -n TicketBooking

# 四個分層專案
dotnet new classlib -n TicketBooking.Domain
dotnet new classlib -n TicketBooking.Application
dotnet new classlib -n TicketBooking.Infrastructure
dotnet new webapi -n TicketBooking.Api

# 測試專案(放在 backend/tests/ 底下,不跟四層混在一起)
mkdir tests
dotnet new xunit -n TicketBooking.UnitTests -o tests/TicketBooking.UnitTests
dotnet new xunit -n TicketBooking.IntegrationTests -o tests/TicketBooking.IntegrationTests

# 加進 solution
dotnet sln add TicketBooking.Domain TicketBooking.Application TicketBooking.Infrastructure TicketBooking.Api
dotnet sln add tests/TicketBooking.UnitTests tests/TicketBooking.IntegrationTests

# 專案間參考(依賴方向:Api → Infrastructure → Application → Domain)
dotnet add TicketBooking.Application reference TicketBooking.Domain
dotnet add TicketBooking.Infrastructure reference TicketBooking.Application TicketBooking.Domain
dotnet add TicketBooking.Api reference TicketBooking.Infrastructure TicketBooking.Application TicketBooking.Domain

dotnet add tests/TicketBooking.UnitTests reference TicketBooking.Application TicketBooking.Domain
dotnet add tests/TicketBooking.IntegrationTests reference TicketBooking.Infrastructure TicketBooking.Api

# UnitTests 加入 NSubstitute(mock interface,如 IOrderRepository)與 Shouldly(斷言可讀性更高)
# 說明:Moq 在 2023 年爆出 SponsorLink 隱私爭議,FluentAssertions v8 起改成商用付費授權,
# 2026 年許多新專案改採 NSubstitute + Shouldly,兩者皆免費、無爭議、持續維護
cd tests/TicketBooking.UnitTests
dotnet add package NSubstitute
dotnet add package Shouldly
cd ../..

# 各專案加入必要套件(注意:Domain 專案不加任何套件,保持零依賴)

# EF Core 套件版本要 pin 住配合 .NET 9(參考你 WorkItemBackend 的作法)
cd TicketBooking.Infrastructure
dotnet add package Microsoft.EntityFrameworkCore.Tools --version 9.0.*
dotnet add package Microsoft.EntityFrameworkCore.Design --version 9.0.*
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL --version 9.0.*
dotnet add package StackExchange.Redis
dotnet add package RabbitMQ.Client
cd ..

cd TicketBooking.Api
dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer
# .NET 9 移除了 Swashbuckle,官方原生方案是 Microsoft.AspNetCore.OpenApi(產生 spec)+ Scalar(UI),
# 不需要另外裝 Swashbuckle,這是目前多數團隊採用的組合
dotnet add package Microsoft.AspNetCore.OpenApi
dotnet add package Scalar.AspNetCore
cd ..

cd tests/TicketBooking.IntegrationTests
dotnet add package Testcontainers.PostgreSql
dotnet add package Testcontainers.RabbitMq
dotnet add package Microsoft.AspNetCore.Mvc.Testing
cd ../..
```

`Program.cs` 裡加上(取代舊版 `AddSwaggerGen()` / `UseSwaggerUI()`):
```csharp
// 在 Program.cs 的 builder 階段加入:
builder.Services.AddOpenApi();

// CORS 設定(允許本機前端呼叫後端 API,對應 10 節排查項)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowLocalhost",
        policy => policy
            .WithOrigins("http://localhost:3000")
            .AllowAnyMethod()
            .AllowAnyHeader());
});

// JWT 認證(對應 docs/adr/005-api-versioning-and-rbac.md)
var jwtSecret = builder.Configuration["Jwt:SecretKey"];
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateAudience = false,
            ValidateIssuer = false
        };
    });

var app = builder.Build();

// 在 app 階段加入:
app.UseAuthentication();
app.UseCors("AllowLocalhost");
app.MapOpenApi();
app.MapScalarApiReference(); // Scalar UI 預設路徑:http://localhost:5000/scalar/v1
```

---

## 3. 建立前端專案

```bash
cd ../../  # 回到 repo 根目錄
npx create-next-app@latest frontend --typescript --tailwind --eslint --app
```

---

## 4. Docker Compose(本機基礎設施)

在 repo 根目錄建立 `docker-compose.yml`:

```yaml
version: '3.9'

services:
  postgres:
    image: postgres:16
    container_name: ticket-postgres
    environment:
      POSTGRES_USER: ticket_admin
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
      POSTGRES_DB: ticket_booking
    ports:
      - "5432:5432"
    volumes:
      - pgdata:/var/lib/postgresql/data

  redis:
    image: redis:7-alpine
    container_name: ticket-redis
    ports:
      - "6379:6379"

  rabbitmq:
    image: rabbitmq:3-management-alpine
    container_name: ticket-rabbitmq
    ports:
      - "5672:5672"     # AMQP
      - "15672:15672"   # Management UI: http://localhost:15672
    environment:
      RABBITMQ_DEFAULT_USER: ${RABBITMQ_USER}
      RABBITMQ_DEFAULT_PASS: ${RABBITMQ_PASSWORD}

volumes:
  pgdata:
```

**注意**:密碼用 `${VARIABLE}` 從 `.env` 檔案讀取,不要寫死在 `docker-compose.yml` 裡(這個檔案通常會 commit)。

### 建立 `.env`(這個檔案絕對不能 commit)
```bash
cat > .env << 'EOF'
POSTGRES_PASSWORD=your_local_dev_password
RABBITMQ_USER=admin
RABBITMQ_PASSWORD=your_local_dev_password
EOF
```

### 啟動基礎設施
```bash
# OrbStack 會自動處理 Apple Silicon 的相容性,指令跟一般 docker compose 一樣
docker compose up -d

# 確認狀態
docker compose ps

# 看某個 service 的 log
docker compose logs -f postgres

# 關閉(不會刪資料)
docker compose stop

# 完全清掉(含資料,重新開始用)
docker compose down -v
```

---

## 5. 機密資訊管理(重點:不能進 git 的東西怎麼處理)

### 5.1 `.gitignore` 一定要包含

在 repo 根目錄建立 `.gitignore`:

```gitignore
# 環境變數 / 機密
.env
.env.local
**/appsettings.Development.json
**/appsettings.Local.json

# .NET
backend/**/bin/
backend/**/obj/

# Node
frontend/node_modules/
frontend/.next/

# DB 相關
*.pgdata

# OS
.DS_Store
```

### 5.2 後端機密改用 `dotnet user-secrets`(不要放在 appsettings.json 裡)

```bash
cd backend/TicketBooking.Api
dotnet user-secrets init

# 設定 JWT 密鑰、連線字串等敏感資訊
dotnet user-secrets set "Jwt:SecretKey" "your-super-secret-key-at-least-32-chars"
dotnet user-secrets set "ConnectionStrings:Postgres" "Host=localhost;Database=ticket_booking;Username=ticket_admin;Password=your_local_dev_password"
dotnet user-secrets set "Redis:ConnectionString" "localhost:6379"
dotnet user-secrets set "RabbitMq:Host" "localhost"
dotnet user-secrets set "RabbitMq:Username" "admin"
dotnet user-secrets set "RabbitMq:Password" "your_local_dev_password"

# 查看目前設定了什麼(這些內容實際存在你電腦的使用者目錄下,不在 repo 裡)
dotnet user-secrets list
```

`user-secrets` 實際存放位置(macOS):
```
~/.microsoft/usersecrets/<user_secrets_id>/secrets.json
```
這個路徑**不在你的 repo 資料夾內**,所以完全不會被 git 追蹤到,是本機開發最推薦的做法(比自己手動排除 appsettings.json 更保險)。

### 5.3 前端機密

Next.js 用 `.env.local`(已經在上面 `.gitignore` 裡排除掉了):
```bash
cd frontend
cat > .env.local << 'EOF'
NEXT_PUBLIC_API_BASE_URL=http://localhost:5000/api
EOF
```
注意:`NEXT_PUBLIC_` 開頭的變數會被打包進前端 bundle,**任何人打開瀏覽器都看得到**,所以只能放「非機密」的設定(如 API URL),真正的密鑰(如果前端需要呼叫第三方服務)不要用這個前綴。

---

## 6. 資料庫 Migration(對應 `docs/specs/data-model.md`)

在 Clean Architecture 下,`AppDbContext` 放在 `TicketBooking.Infrastructure`,但啟動設定(連線字串等)在 `TicketBooking.Api`,所以下指令時要用 `--project` 和 `--startup-project` 指定:

```bash
cd backend

# 安裝 EF Core CLI 工具(第一次用需要)
dotnet tool install --global dotnet-ef

# 建立第一個 migration(根據 data-model.md 定義好 Entity 後執行)
dotnet ef migrations add InitialCreate \
  --project TicketBooking.Infrastructure \
  --startup-project TicketBooking.Api

# 套用到本機 Postgres(確保 docker compose up 先跑起來)
dotnet ef database update \
  --project TicketBooking.Infrastructure \
  --startup-project TicketBooking.Api
```

Migration 檔案會產生在 `TicketBooking.Infrastructure/Migrations/`,跟 `docs/adr/007-clean-architecture-layering.md` 定義的目錄結構一致。

用 DBeaver 連線確認:
- Host: `localhost`
- Port: `5432`
- Database: `ticket_booking`
- User/Password: 跟 `.env` 裡設定的一致

---

## 7. 啟動整個系統(開發模式)

開三個終端機視窗:

```bash
# 視窗 1:基礎設施(如果還沒啟動)
docker compose up -d

# 視窗 2:後端
cd backend/TicketBooking.Api
dotnet watch run
# 預設會在 http://localhost:5000,API 文檔(Scalar UI)在 http://localhost:5000/scalar/v1

# 視窗 3:前端
cd frontend
npm install
npm run dev
# 預設在 http://localhost:3000
```

---

## 8. 測試

### 8.1 單元測試 + 整合測試(Testcontainers)

測試專案已經在第 2 節建立好(`tests/TicketBooking.UnitTests`、`tests/TicketBooking.IntegrationTests`),對應 `docs/adr/007-clean-architecture-layering.md` 的分層:

- `UnitTests`:測 `TicketBooking.Application` 的 Service(mock `TicketBooking.Application/Interfaces/` 底下的 interface,如 `IOrderRepository`)
- `IntegrationTests`:用 Testcontainers 測 `TicketBooking.Infrastructure` 的真實 DB/MQ 行為

```bash
cd backend

# 執行全部測試
dotnet test

# 只跑單元測試
dotnet test tests/TicketBooking.UnitTests

# 只跑整合測試(Testcontainers 會自動用 OrbStack 拉起暫時性的測試容器,測完自動銷毀)
dotnet test tests/TicketBooking.IntegrationTests
```

**注意**:Testcontainers 需要 Docker daemon 在跑,OrbStack 已經提供相容的 daemon,不需要額外設定。

### 8.2 前端測試

```bash
cd frontend
npm install -D vitest @testing-library/react @testing-library/jest-dom
npm run test
```

---

## 9. 完整開發流程總結(你接下來實際會做的順序)

```
1. docker compose up -d                    # 起基礎設施
2. dotnet ef database update               # 套 migration
3. dotnet watch run (backend)              # 起後端,邊改邊自動 reload
4. npm run dev (frontend)                  # 起前端
5. 用 Scalar UI(http://localhost:5000/scalar/v1) 或 Thunder Client 手動測 API
6. dotnet test                             # 跑自動化測試
7. k6 run load-tests/xxx.js                # 壓測(參考 load-testing-plan.md)
8. git add . && git commit                 # 因為 .gitignore 設好了,機密不會被 commit 進去
```

---

## 10. 常見卡關排查

| 問題 | 排查方向 |
|---|---|
| `dotnet ef database update` 連不上 DB | 確認 `docker compose ps` 裡 postgres 是 healthy,且 user-secrets 裡連線字串的 port/密碼正確 |
| OrbStack container 起不來 | `docker compose logs <service名稱>` 看詳細錯誤,常見是 port 被本機其他程式佔用(`lsof -i :5432`) |
| Testcontainers 測試跑很慢 | 確認 OrbStack 資源設定(Settings → Resources)有給足夠 CPU/記憶體,但不用超過 24GB 的一半,留給其他程式 |
| 前端呼叫後端 CORS 錯誤 | 後端 `Program.cs` 要加 `AddCors` 允許 `http://localhost:3000` |