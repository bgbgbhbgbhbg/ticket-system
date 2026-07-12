# Task 3: Auth 功能 - 開發會話操作日誌

## 會話信息
- **日期**: 2026-07-12
- **任務**: Task 3 - Auth 功能完整實現與測試
- **狀態**: ✅ 完成

---

## 1. 問題診斷與解決

### 問題 1: 前端導入路徑錯誤
**症狀**: 
```
Module not found: Can't resolve '../lib/auth-context'
```
**位置**: `frontend/app/auth/register/page.tsx` Line 6

**根本原因**: 
從 `/app/auth/register/page.tsx` 到 `/app/lib/auth-context.tsx` 的路徑應為 `../../lib/auth-context`，而不是 `../lib/auth-context`

**修正操作**: 
```
OLD: import { useAuth } from '../lib/auth-context';
NEW: import { useAuth } from '../../lib/auth-context';
```

**檔案**: [frontend/app/auth/register/page.tsx](frontend/app/auth/register/page.tsx#L6)

### 問題 2: API URL 拼接錯誤
**症狀**: 
Tickets 列表無法加載（前面對話中的回歸問題）

**根本原因**: 
`frontend/app/lib/api.ts` 重複加了 `/api` 路徑：
- `.env.local` 已設置: `NEXT_PUBLIC_API_URL=http://localhost:5263/api/v1`
- `api.ts` 又加上: `API_BASE_URL + '/api'` → `http://localhost:5263/api/api/v1`

**修正操作**: 
```typescript
// OLD: 
const API_BASE_URL = (process.env.NEXT_PUBLIC_API_URL ? process.env.NEXT_PUBLIC_API_URL + '/api' : 'http://localhost:5263/api');
const API_VERSION = 'v1';
fullUrl: `${API_BASE_URL}/${API_VERSION}/tickets`

// NEW:
const API_BASE_URL = process.env.NEXT_PUBLIC_API_URL || 'http://localhost:5263/api/v1';
fullUrl: `${API_BASE_URL}/tickets`
```

**檔案**: [frontend/app/lib/api.ts](frontend/app/lib/api.ts#L1-L20)

### 問題 3: /tickets 路由不存在
**症狀**: 
註冊成功後重定向到 `/tickets` 返回 404

**根本原因**: 
`frontend/app/tickets/` 目錄存在但沒有 `page.tsx` 文件

**修正操作**: 
建立 `frontend/app/tickets/page.tsx` 重定向回主頁：

```typescript
'use client';

import { useEffect } from 'react';
import { useRouter } from 'next/navigation';

export default function TicketsPage() {
  const router = useRouter();

  useEffect(() => {
    router.push('/');
  }, [router]);

  return null;
}
```

**檔案**: [frontend/app/tickets/page.tsx](frontend/app/tickets/page.tsx) (新建)

---

## 2. 測試流程執行

### 2.1 環境準備
```bash
# Terminal 1: 啟動後端 API
cd /Users/dev/Projects/github/ticket-system/backend/TicketBooking.Api
dotnet run
# Output: listening on http://localhost:5263 ✅

# Terminal 2: 啟動前端開發服務器
cd /Users/dev/Projects/github/ticket-system/frontend
npm run dev
# Output: ready on http://localhost:3000 ✅
```

### 2.2 完整測試用例

#### 測試 1: 主頁加載
- **操作**: 導航到 http://localhost:3000/
- **驗證**:
  - ✅ Tickets 列表正常顯示（3 個票券）
  - ✅ 主標題: "高併發票券預訂系統"
  - ✅ 登入 + 註冊按鈕可見
  
#### 測試 2: 用戶註冊 (新用戶)
- **操作**:
  1. 點擊「註冊」
  2. 填寫表單:
     - displayName: "John Doe"
     - email: "johndoe@example.com"
     - password: "securePassword123"
  3. 點擊「註冊」按鈕
- **驗證**:
  - ✅ HTTP 201 Created
  - ✅ 自動登錄
  - ✅ 重定向到主頁 (/)
  - ✅ 顯示 "歡迎, John Doe"
  - ✅ 登出按鈕可見

#### 測試 3: 用戶登出
- **操作**: 點擊「登出」按鈕
- **驗證**:
  - ✅ localStorage 已清除 auth_token
  - ✅ 顯示登入 + 註冊按鈕
  - ✅ Tickets 列表仍然顯示

#### 測試 4: 用戶登入 (有效憑證)
- **操作**:
  1. 點擊「登入」
  2. 填寫表單:
     - email: "johndoe@example.com"
     - password: "securePassword123"
  3. 點擊「登入」
- **驗證**:
  - ✅ HTTP 200 OK
  - ✅ JWT token 已保存
  - ✅ 重定向到主頁 (/)
  - ✅ 顯示 "歡迎, John Doe"

#### 測試 5: 無效密碼登入
- **操作**:
  1. 導航到 /auth/login
  2. 填寫表單:
     - email: "johndoe@example.com"
     - password: "wrongPassword"
  3. 點擊「登入」
- **驗證**:
  - ✅ HTTP 401 Unauthorized
  - ✅ 顯示錯誤: "Invalid email or password"
  - ✅ 不重定向

#### 測試 6: 不存在的 Email 登入
- **操作**:
  1. 在登入頁面
  2. 填寫表單:
     - email: "nonexistent@example.com"
     - password: "anyPassword123"
  3. 點擊「登入」
- **驗證**:
  - ✅ HTTP 401 Unauthorized
  - ✅ 顯示相同錯誤信息（不洩漏用戶存在性）

#### 測試 7: 重複 Email 註冊
- **操作**:
  1. 導航到 /auth/register
  2. 填寫表單 (使用已存在的 email):
     - displayName: "Jane Smith"
     - email: "johndoe@example.com"
     - password: "anotherPassword123"
  3. 點擊「註冊」
- **驗證**:
  - ✅ HTTP 409 Conflict
  - ✅ 顯示錯誤: "Email 'johndoe@example.com' is already registered"
  - ✅ 不重定向

#### 測試 8: 第二個用戶註冊
- **操作**:
  1. 刷新頁面 /auth/register
  2. 填寫表單:
     - displayName: "Another User"
     - email: "anotheruser@example.com"
     - password: "validPassword123"
  3. 點擊「註冊」
- **驗證**:
  - ✅ HTTP 201 Created
  - ✅ 自動登錄
  - ✅ 重定向到主頁
  - ✅ 顯示 "歡迎, Another User"

---

## 3. 前端修改整理

| 文件 | 操作 | 原因 |
|------|------|------|
| [frontend/app/auth/register/page.tsx](frontend/app/auth/register/page.tsx#L6) | 修正導入路徑 | `../lib/auth-context` → `../../lib/auth-context` |
| [frontend/app/lib/api.ts](frontend/app/lib/api.ts#L1-L20) | 修正 URL 拼接邏輯 | 移除重複的 `/api` 路徑 |
| [frontend/app/tickets/page.tsx](frontend/app/tickets/page.tsx) | 新建文件 | 支持註冊後重定向 |

---

## 4. 後端 API 驗證

### API 端點確認
```
✅ POST /api/v1/auth/register (201 Created)
✅ POST /api/v1/auth/login (200 OK)
✅ GET /api/v1/auth/me (200 OK, 需 [Authorize])
✅ GET /api/v1/tickets (200 OK, 無認證需求)
```

### 資料庫操作日誌
```sql
-- 測試期間建立的 2 個用戶:
INSERT INTO users (email, display_name, password_hash, role, ...)
VALUES ('johndoe@example.com', 'John Doe', '$2a$...', 'User', ...)

INSERT INTO users (email, display_name, password_hash, role, ...)
VALUES ('anotheruser@example.com', 'Another User', '$2a$...', 'User', ...)

-- 所有查詢執行成功，無錯誤
```

---

## 5. 前端功能驗證

### AuthProvider 工作流程
```typescript
// 1. 用戶註冊
register(email, password, displayName)
  → POST /auth/register
  → 自動調用 login()
  → 保存 token 到 localStorage
  → 重定向到 /tickets

// 2. 用戶登入  
login(email, password)
  → POST /auth/login
  → 接收 { accessToken, expiresIn }
  → 保存 token 到 localStorage
  → 重定向到 /tickets

// 3. 頁面刷新 (Session 恢復)
useEffect 初始化:
  → 檢查 localStorage auth_token
  → 若存在，調用 GET /auth/me 驗證
  → 若有效，恢復 user 狀態
  → isLoading 切回 false

// 4. 用戶登出
logout()
  → 清除 localStorage auth_token
  → 清除 state
```

### 組件樹結構驗證
```
<RootLayout>
  <AuthProvider>
    <Page /> (home)
      ├─ TicketList ✅ (不需認證)
      └─ Auth UI (登入/註冊按鈕 or 用戶菜單)
    
    <AuthPages>
      ├─ /auth/register/page.tsx ✅
      ├─ /auth/login/page.tsx ✅
      └─ (useAuth Hook 正常工作)
    
    <TicketsPage>
      └─ Redirect to / ✅
```

---

## 6. 後端單元測試確認

### AuthServiceTests
```bash
dotnet test TicketBooking.UnitTests/Services/AuthServiceTests.cs
```

**測試結果**: 5/5 ✅ PASS
```
✅ RegisterAsync_WithNewEmail_ShouldCreateUser
✅ RegisterAsync_WithExistingEmail_ShouldThrowEmailAlreadyExistsException
✅ LoginAsync_WithValidCredentials_ShouldReturnUserAndToken
✅ LoginAsync_WithNonexistentEmail_ShouldThrowInvalidCredentialsException
✅ LoginAsync_WithWrongPassword_ShouldThrowInvalidCredentialsException
```

---

## 7. 性能與安全性檢查

### 密碼安全
- ✅ BCrypt 加密 (workFactor: 12)
- ✅ 密碼驗證使用 constant-time 比較
- ✅ PasswordHash 不在 API 回應中

### JWT 安全
- ✅ 使用 HS256 (HMAC with SHA256)
- ✅ 聲明包含: sub, email, role, exp
- ✅ 1 小時過期 (3600 seconds)
- ✅ 秘鑰來自 dotnet user-secrets (不硬編碼)

### 錯誤信息安全
- ✅ 登入失敗使用通用訊息 (不洩漏用戶存在性)
- ✅ 409 衝突包含 email (安全)
- ✅ 無 SQL 錯誤洩漏到客戶端

---

## 8. 已知限制與待辦項

### 限制:
1. 密碼重設功能未實現 (Task 3 不要求)
2. 郵件驗證未實現 (Task 3 不要求)
3. RBAC 角色檢查未實現 (Task 7 才實現)

### 待辦:
- [ ] Task 4: Orders 創建 (POST /orders with idempotency)
- [ ] Task 5: Orders 異步處理 (RabbitMQ)
- [ ] Task 6: Orders 集成測試
- [ ] Task 7: Admin 角色 RBAC

---

## 9. 部署建議

### 生產環境配置
```bash
# 後端
export JWT_SECRET="your-very-long-random-secret-key-min-32-chars"
export ASPNETCORE_ENVIRONMENT="Production"
export ConnectionStrings__DefaultConnection="postgresql://..."

# 前端
NEXT_PUBLIC_API_URL=https://api.production.com/api/v1
```

### 資料庫遷移
```bash
# 套用所有待決的遷移
dotnet ef database update --project TicketBooking.Infrastructure --startup-project TicketBooking.Api
```

---

## 10. 會話摘要

| 項目 | 狀態 | 備註 |
|------|------|------|
| 問題診斷 | ✅ 完成 | 找到並修正 3 個問題 |
| 前端修正 | ✅ 完成 | 導入路徑 + URL 拼接 + 路由 |
| 環境準備 | ✅ 完成 | 後端 + 前端都已啟動 |
| 功能測試 | ✅ 100% | 8 個完整測試用例 |
| 錯誤測試 | ✅ 100% | 無效密碼、重複 email 等 |
| 文檔記錄 | ✅ 完成 | Task3-test-results.md + session log |

**最終狀態**: ✅ **Task 3 完成，所有測試通過**

---

## 11. 快速參考

### 開發命令
```bash
# 後端
cd backend/TicketBooking.Api && dotnet run
dotnet test TicketBooking.UnitTests

# 前端  
cd frontend && npm run dev
npm run build

# 資料庫
dotnet ef migrations add <名稱> --project TicketBooking.Infrastructure
dotnet ef database update
```

### 測試帳戶
```
Email: johndoe@example.com
Password: securePassword123

Email: anotheruser@example.com
Password: validPassword123
```

### 接下來的 Task
下一個任務是 **Task 4: Orders 創建** - 實現購票訂單創建 API，包含冪等性鍵和 RabbitMQ 消息隊列。

