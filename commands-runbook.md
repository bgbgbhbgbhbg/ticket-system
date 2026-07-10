# Commands Runbook — 快速指令參考

> 本文件彙集 SETUP.md 中的所有指令,方便複製貼上執行。**推薦配合 SETUP.md 的概念說明一起閱讀**。

---

## 1. 前置工具檢查

```bash
# 確認既有工具版本(應該都已經裝好)
dotnet --version   # 應顯示 9.x
node --version
which docker       # 或 which orbstack

# 安裝 k6(壓測工具)
brew install k6
k6 version
```

---

## 2. 後端完整設置(第一次)

### 2.1 基礎專案架構

```bash
# 在 repo 根目錄執行
mkdir backend && cd backend

# 建立 solution
dotnet new sln -n TicketBooking

# 建立四個分層專案
dotnet new classlib -n TicketBooking.Domain
dotnet new classlib -n TicketBooking.Application
dotnet new classlib -n TicketBooking.Infrastructure
dotnet new webapi -n TicketBooking.Api

# 建立測試專案
mkdir tests
dotnet new xunit -n TicketBooking.UnitTests -o tests/TicketBooking.UnitTests
dotnet new xunit -n TicketBooking.IntegrationTests -o tests/TicketBooking.IntegrationTests

# 加進 solution
dotnet sln add TicketBooking.Domain TicketBooking.Application TicketBooking.Infrastructure TicketBooking.Api
dotnet sln add tests/TicketBooking.UnitTests tests/TicketBooking.IntegrationTests
```

### 2.2 設定專案間參考(依賴方向)

```bash
# 依賴方向:Api → Infrastructure → Application → Domain
dotnet add TicketBooking.Application reference TicketBooking.Domain
dotnet add TicketBooking.Infrastructure reference TicketBooking.Application TicketBooking.Domain
dotnet add TicketBooking.Api reference TicketBooking.Infrastructure TicketBooking.Application TicketBooking.Domain

# 測試專案參考
dotnet add tests/TicketBooking.UnitTests reference TicketBooking.Application TicketBooking.Domain
dotnet add tests/TicketBooking.IntegrationTests reference TicketBooking.Infrastructure TicketBooking.Api
```

### 2.3 單元測試套件(NSubstitute + Shouldly)

```bash
cd tests/TicketBooking.UnitTests
dotnet add package NSubstitute
dotnet add package Shouldly
cd ../..
```

### 2.4 Application 層套件

```bash
# cd TicketBooking.Application
# dotnet add package FluentValidation
# cd ..
```

### 2.5 Infrastructure 層套件(EF Core + Redis + RabbitMQ)

```bash
cd TicketBooking.Infrastructure
# EF Core Tools
dotnet add package Microsoft.EntityFrameworkCore.Tools --version 9.0.*
dotnet add package Microsoft.EntityFrameworkCore.Design --version 9.0.*
# PostgreSQL driver
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL --version 9.0.*
# Redis
dotnet add package StackExchange.Redis
# RabbitMQ
dotnet add package RabbitMQ.Client
cd ..
```

### 2.6 API 層套件(認證 + OpenAPI)

```bash
cd TicketBooking.Api
dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer
# .NET 9 官方 OpenAPI + Scalar 組合(不用 Swashbuckle)
dotnet add package Microsoft.AspNetCore.OpenApi
dotnet add package Scalar.AspNetCore
cd ..
```

### 2.7 整合測試套件(Testcontainers)

```bash
cd tests/TicketBooking.IntegrationTests
dotnet add package Testcontainers.PostgreSql
dotnet add package Testcontainers.RabbitMq
dotnet add package Microsoft.AspNetCore.Mvc.Testing
cd ../..
```

---

## 3. 前端完整設置

```bash
# 回到 repo 根目錄
cd ../../

# 建立 Next.js 專案
npx create-next-app@latest frontend --typescript --tailwind --eslint --app

# 進入前端目錄(後續操作)
cd frontend
```

---

## 4. Docker Compose 基礎設施

### 4.1 啟動所有服務

```bash
# 回到 repo 根目錄(應該有 docker-compose.yml)
cd ../../

# 啟動(背景執行)
docker compose up -d

# 確認所有 service 都 healthy
docker compose ps

# 檢查特定 service 的 log
docker compose logs -f postgres
docker compose logs -f redis
docker compose logs -f rabbitmq
```

### 4.2 停止 / 完全清理

```bash
# 只停止(保留資料)
docker compose stop

# 完全刪除(含 volume,重新開始)
docker compose down -v

# 如果某個 service 有問題,重啟它
docker compose restart postgres
```

---

## 5. 機密資訊設置

### 5.1 建立 `.env` 檔(絕對不能 commit)

```bash
# 在 repo 根目錄
cat > .env << 'EOF'
POSTGRES_PASSWORD=your_local_dev_password
RABBITMQ_USER=admin
RABBITMQ_PASSWORD=your_local_dev_password
EOF

# 驗證已建立
cat .env
```

### 5.2 建立 .gitignore

```bash
cat > .gitignore << 'EOF'
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
EOF
```

### 5.3 後端 user-secrets 設定

```bash
cd backend/TicketBooking.Api

# 初始化(第一次用)
dotnet user-secrets init

# 設定所有敏感資訊
dotnet user-secrets set "Jwt:SecretKey" "your-super-secret-key-at-least-32-chars"
dotnet user-secrets set "ConnectionStrings:Postgres" "Host=localhost;Database=ticket_booking;Username=ticket_admin;Password=your_local_dev_password"
dotnet user-secrets set "Redis:ConnectionString" "localhost:6379"
dotnet user-secrets set "RabbitMq:Host" "localhost"
dotnet user-secrets set "RabbitMq:Username" "admin"
dotnet user-secrets set "RabbitMq:Password" "your_local_dev_password"

# 驗證設定
dotnet user-secrets list

# 回到 repo 根目錄
cd ../../..
```

### 5.4 前端環境變數

```bash
cd frontend

# 建立 .env.local(同樣不會被 commit)
cat > .env.local << 'EOF'
NEXT_PUBLIC_API_BASE_URL=http://localhost:5000/api
EOF

# 回到 repo 根目錄
cd ../..
```

---

## 6. 資料庫 Migration

### 6.1 第一次安裝 EF Core 工具

```bash
cd backend

# 全域安裝(只需一次)
dotnet tool install --global dotnet-ef

# 驗證安裝
dotnet ef --version
```

### 6.2 建立 & 套用 Migration

```bash
# 確保已 cd 到 backend 目錄
cd backend

# 第一次 migration(Entity 定義完後執行)
dotnet ef migrations add InitialCreate \
  --project TicketBooking.Infrastructure \
  --startup-project TicketBooking.Api

# 套用到本機 PostgreSQL(確保 docker compose up 已執行)
dotnet ef database update \
  --project TicketBooking.Infrastructure \
  --startup-project TicketBooking.Api

# 驗證:用 DBeaver 連線
# Host: localhost
# Port: 5432
# Database: ticket_booking
# User: ticket_admin
# Password: (跟 .env 一致)
```

### 6.3 後續 Migration(修改 schema 時)

```bash
cd backend

# 建立新的 migration(描述具體改動)
dotnet ef migrations add AddOrderStatusColumn \
  --project TicketBooking.Infrastructure \
  --startup-project TicketBooking.Api

# 套用
dotnet ef database update \
  --project TicketBooking.Infrastructure \
  --startup-project TicketBooking.Api

# 查看所有已套用的 migration
dotnet ef migrations list \
  --project TicketBooking.Infrastructure \
  --startup-project TicketBooking.Api
```

---

## 7. 啟動整個系統(開發模式)

### 7.1 三個終端機視窗

```bash
# 終端 1:基礎設施
docker compose up -d
docker compose ps

# 終端 2:後端(自動 reload)
cd backend/TicketBooking.Api
dotnet watch run
# 訪問: http://localhost:5000/scalar/v1

# 終端 3:前端
cd frontend
npm install
npm run dev
# 訪問: http://localhost:3000
```

### 7.2 也可以單獨啟動某一部分

```bash
# 只啟動後端(不自動 reload)
cd backend/TicketBooking.Api
dotnet run

# 只啟動前端
cd frontend
npm run dev
```

---

## 8. 測試

### 8.1 執行全部測試

```bash
cd backend

# 全部(單元 + 整合)
dotnet test

# 詳細輸出
dotnet test --verbosity detailed

# 看覆蓋率(需要額外套件,見下節)
dotnet test /p:CollectCoverage=true
```

### 8.2 只跑單元測試

```bash
cd backend

# 只跑 UnitTests
dotnet test tests/TicketBooking.UnitTests

# 特定測試類別
dotnet test tests/TicketBooking.UnitTests --filter "TestClass=OrderServiceTests"

# 特定測試方法
dotnet test tests/TicketBooking.UnitTests --filter "Name~CreateOrder"
```

### 8.3 只跑整合測試(Testcontainers)

```bash
cd backend

# 只跑 IntegrationTests(會自動拉起測試用的 Docker 容器)
dotnet test tests/TicketBooking.IntegrationTests

# 看詳細 log
dotnet test tests/TicketBooking.IntegrationTests -v detailed
```

### 8.4 前端測試

```bash
cd frontend

# 首次安裝
npm install -D vitest @testing-library/react @testing-library/jest-dom

# 執行測試
npm run test

# 監視模式(改程式碼自動重跑)
npm run test -- --watch
```

---

## 9. 壓測(k6)

```bash
cd load-tests

# 查看可用的壓測腳本
ls *.js

# 執行某個壓測(詳見 load-testing-plan.md)
k6 run order-creation-load-test.js

# 本機簡單模式(1 VU,30 秒)
k6 run --vus 1 --duration 30s order-creation-load-test.js

# 漸進式負載(從 1 VU 上升到 100 VU)
k6 run --stage 30s:1 --stage 30s:100 order-creation-load-test.js
```

---

## 10. Git 提交流程

```bash
# 回到 repo 根目錄
cd ../../

# 檢查哪些檔案會被提交(應該看不到 .env、appsettings.Development.json 等)
git status

# 正常的提交流程
git add .
git diff --cached  # 雙重檢查
git commit -m "feat: add order creation service with optimistic locking"
git push

# 如果不小心 staged 了機密檔案,撤銷
git reset HEAD .env
git reset HEAD backend/**/appsettings.Development.json
```

---

## 11. 清理 & 重新開始

### 11.1 刪除本機開發資料(保留程式碼)

```bash
# 清除所有編譯產物
cd backend
dotnet clean
cd ../

# 清除 Node 相關
rm -rf frontend/node_modules frontend/.next

# 清除所有 Docker 資源
docker compose down -v
```

### 11.2 完全重置(從頭開始)

```bash
# 回到 repo 根目錄
cd ../../

# 移除 backend 和 frontend 目錄重新生成(警告:會刪除所有程式碼改動!)
rm -rf backend frontend

# 從 SETUP.md 第 2、3 節重新開始
```

---

## 12. 常見指令組合

### 開發者早上第一件事

```bash
# 終端 1
docker compose up -d && docker compose ps

# 終端 2
cd backend && dotnet watch run

# 終端 3
cd frontend && npm run dev

# 檢查日誌
docker compose logs -f postgres
```

### 提交前檢查清單

```bash
# 確認測試全過
dotnet test

# 確認沒有把機密檔案 staged
git status | grep ".env\|appsettings.Development.json"

# 提交
git add . && git commit -m "..."
```

### 偵錯某個 API 錯誤

```bash
# 看後端 log
docker compose logs -f | grep -i error

# 看資料庫狀態
# 用 DBeaver 連線並查詢表

# 看 Redis 快取
redis-cli -h localhost -p 6379
KEYS *
```

---

## 13. 環境變數快速参考

### 後端(via user-secrets)

```
Jwt:SecretKey                    = your-super-secret-key-at-least-32-chars
ConnectionStrings:Postgres       = Host=localhost;Database=ticket_booking;Username=ticket_admin;Password=...
Redis:ConnectionString           = localhost:6379
RabbitMq:Host                    = localhost
RabbitMq:Username                = admin
RabbitMq:Password                = ...
```

### 前端(.env.local)

```
NEXT_PUBLIC_API_BASE_URL=http://localhost:5000/api
```

### Docker Compose(.env)

```
POSTGRES_PASSWORD=...
RABBITMQ_USER=admin
RABBITMQ_PASSWORD=...
```

---

## 提示

- 💡 大多數指令都可以複製貼上直接執行,但路徑(`cd backend`等)要確保正確
- 📝 如果某個指令執行失敗,先看錯誤訊息,參考 SETUP.md 第 10 節的「常見卡關排查」
- 🔑 **絕對不要在程式碼或 Git commit 中包含機密資訊**,使用 user-secrets 或 .env
- 🐋 OrbStack 會自動處理 Apple Silicon 相容性,指令跟一般 Docker 相同
