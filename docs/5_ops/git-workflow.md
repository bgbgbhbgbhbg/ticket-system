# Git Workflow & CI/PR Setup

> 定義這個專案的分支策略、commit 規範、CI 設定方式、PR 相關的 GitHub 網頁設定。單人開發,但刻意保留 PR 流程作為工程紀律的練習與展示。

---

## 0. 現在的例外:初始骨架直接 commit 到 main

在你還在建立**專案骨架**(docs 文件、四層專案結構、第一個 DB migration)的階段,**直接 commit 到 main,不用開分支**。理由:這個階段還沒有「功能」可言,不存在「這個分支做這個功能、那個分支做那個功能」的平行開發需求,開分支反而是多餘的儀式。

**例外結束的時間點**:等你要開始寫**第一個功能**(如 Tickets CRUD)時,從那一刻起,所有改動都走下面第 2 節的分支流程,不再直接 commit 到 main。

建議這個階段的 commit 拆法(對照你目前的進度):
```bash
git commit -m "docs: 完成規格驅動開發的完整文件(PRD/Architecture/specs/ADR)"
git commit -m "chore: 建立 Clean Architecture 四層專案骨架"
git commit -m "feat: 定義 Entity 並建立 InitialCreate migration"
git commit -m "ci: 加入 GitHub Actions 最小可行 pipeline"
```

---

## 1. 分支策略:簡化版 GitHub Flow(不是完整 Git Flow)

**不採用**傳統 Git Flow(`main` + `develop` + `release/*` + `hotfix/*` 那一整套),**採用**簡化版 GitHub Flow:只有 `main` + 短命的 `feature/*` 分支。

**為什麼**:傳統 Git Flow 是為了「有固定發版週期、需要同時維護多個版本」的專案設計的(例如要同時支援 v1.2 的 hotfix 和 v2.0 的開發)。這個專案是單人 demo,沒有版本並行維護的需求,`develop`/`release` 這些分支只會增加不必要的合併複雜度,對專案呈現也沒有加分——重點在於「有沒有 PR 習慣」,不是「有沒有背誦 Git Flow 那五種分支」。

分支命名規則:

| 分支類型 | 命名 | 範例 |
|---|---|---|
| 功能開發 | `feature/{簡短描述}` | `feature/tickets-crud`、`feature/order-optimistic-lock` |
| 修 bug | `fix/{簡短描述}` | `fix/redis-connection-timeout` |
| 純文件/規格調整 | `docs/{簡短描述}` | `docs/update-error-codes` |

---

## 2. 標準開發流程(骨架建好之後,從第一個功能開始適用)

```bash
# 1. 從最新的 main 切分支
git checkout main
git pull
git checkout -b feature/tickets-crud

# 2. 開發、分次 commit(見第 3 節 commit 規範)
git add .
git commit -m "feat(api): 新增 Ticket entity 與 InitialCreate migration"

# 3. push 分支(不要直接 push 到 main)
git push -u origin feature/tickets-crud
```

接著在 **GitHub 網頁**上:

1. 開一個 PR:base 選 `main`,compare 選 `feature/tickets-crud`
2. PR 頁面右側「Reviewers」欄位,加入 **Copilot** 當 reviewer(GitHub 內建功能,不需要額外設定,只要你的帳號有 Copilot 授權就會出現在選單裡)
3. Copilot 會在幾分鐘內自動掃過 diff,在有疑慮的地方留下 review comment(例如漏處理 null、SQL Injection 風險、命名不一致等)
4. 你自己看過 Copilot 的評論,處理完覺得沒問題,自己點 **"Approve"**,再點 **"Merge pull request"**
5. 建議選 **"Squash and merge"**(不是 "Create a merge commit"),這樣 `main` 的歷史會是一條乾淨的線性記錄,每個 PR 對應剛好一個 commit,任何人看 commit history 時一目瞭然

---

## 3. Commit Message 規範:Conventional Commits + Scope

### 3.1 為什麼要加 scope(這是你問的重點)

單純用 `feat: 新增功能` 這種寫法,在全端專案(backend + frontend + infra + docs 混在一起)裡,光看 commit message 看不出這次改動影響的是哪一層。加上 **scope**(用括號標註影響範圍),是目前業界(尤其全端/monorepo 專案)很普遍的做法,一眼就能看出修改範圍,`git log --oneline` 掃過去也好分類。

### 3.2 格式

```
{type}({scope}): {簡短描述,祈使句,不要句點結尾}

{可選:更詳細的說明,說明「為什麼」而不是「做了什麼」(做了什麼 diff 自己看得出來)}
```

### 3.3 Type 對照表(不變,延續 AGENTS.md 原本的定義)

| type | 使用時機 |
|---|---|
| `feat` | 新增功能 |
| `fix` | 修 bug |
| `docs` | 只改文件,不影響程式行為 |
| `test` | 新增或修改測試,不影響production程式碼 |
| `refactor` | 重構,不改變外部行為 |
| `chore` | 建置工具、依賴套件更新等雜項 |
| `ci` | CI/CD pipeline 設定變更 |

### 3.4 Scope 對照表(本專案適用,對應目錄結構)

| scope | 對應範圍 |
|---|---|
| `api` | `backend/TicketBooking.Api` |
| `domain` | `backend/TicketBooking.Domain` |
| `application` | `backend/TicketBooking.Application` |
| `infra` | `backend/TicketBooking.Infrastructure` |
| `web` | `frontend/` |
| `db` | 資料庫 migration、schema 變更(可能橫跨 infra,但影響夠大值得獨立標註) |
| `ci` | `.github/workflows/` |
| `docs` | `docs/` 底下的規格文件 |

### 3.5 範例(對照你目前的開發階段)

```
feat(domain): Order entity 加入 TransitionTo() 狀態轉換方法
feat(infra): 實作 OrderRepository 樂觀鎖 CAS 查詢
feat(api): 新增 POST /orders 建立訂單 endpoint
fix(infra): 修正 Redis 連線逾時未捕捉例外的問題
test(application): 補上 OrderService 樂觀鎖重試的單元測試
docs(specs): error-codes.md 新增 ORDER_INVALID_STATUS_TRANSITION
ci(ci): CI pipeline 加入 Testcontainers 整合測試 job
db: 新增 InitialCreate migration(Users/Tickets/Orders)
```

**沒有明確 scope 的情況**(例如同時橫跨多個範圍的大改動,或是最初的骨架 commit),可以省略 scope,直接寫 `feat: ...`,不用硬套。

---

## 4. CI 怎麼加(對應你已經有的 `ci.yml`)

### 4.1 放置位置

```bash
mkdir -p .github/workflows
mv ci.yml .github/workflows/ci.yml
```

**路徑是固定的**,GitHub 只認 `.github/workflows/*.yml`,放錯位置不會被觸發。

### 4.2 怎麼確認它有跑

1. `git push` 之後,到 GitHub 網頁的 repo 頁面,點上方的 **"Actions"** 分頁
2. 會看到剛剛的 push 或 PR 觸發了一個 workflow run,點進去看每個 step 是綠勾(成功)還是紅叉(失敗)
3. 紅叉的話點進去看詳細 log,通常是套件還原失敗或測試沒過

### 4.3 之後要加 Testcontainers 整合測試時怎麼升級

現在的 `ci.yml` 只跑 Unit Test,之後要加 Integration Test(需要真的 Postgres/RabbitMQ),GitHub Actions 支援用 `services` 區塊在 runner 上啟動臨時容器,屆時我會幫你把 workflow 改成類似:

```yaml
services:
  postgres:
    image: postgres:16
    env:
      POSTGRES_PASSWORD: test_password
    ports: ["5432:5432"]
  # rabbitmq 同理
```

現階段先不用加,等 Orders 功能真的要寫 Testcontainers 測試時再回來升級。

---

## 5. PR 相關的 GitHub 網頁設定(一次性設定,做一次就好)

### 5.1 設定 Branch Protection Rule(強制走 PR,不能直接 push 到 main)

1. 到 repo 頁面 → **Settings** → 左側選單 **Branches**
2. **"Branch protection rules"** 底下點 **"Add rule"**
3. **"Branch name pattern"** 填 `main`
4. 勾選以下選項:
   - ✅ **"Require a pull request before merging"**(強制走 PR,不能直接 push 到 main)
   - ✅ **"Require status checks to pass before merging"**,並在下方搜尋框選擇你 `ci.yml` 裡定義的 job 名稱(即 `build-and-test`)——這樣 CI 沒過,PR 的 "Merge" 按鈕會被鎖住,按不下去
   - ⬜ **"Require approvals"** 這個可以不勾——因為你是單人專案,沒有其他人可以 approve,勾了反而會把自己鎖死進不去
5. 存檔

### 5.2 讓 Copilot 出現在 PR 的 Reviewer 選單

不需要額外設定,只要:
- 你的 GitHub 帳號有 Copilot 授權(個人版或透過學校/公司)
- Repo 有開啟 Copilot 功能(通常個人 repo 預設就有)

開 PR 時,右側欄位 "Reviewers" 點下拉選單,應該會直接看到 "Copilot" 這個選項,點選加入即可,它會在幾分鐘內自動跑完 review。

---

## 6. 總結:你接下來的具體順序

```
1. (現在)完成 DB 建立 + InitialCreate migration → 直接 commit 到 main(見第 0 節)
2. git push 一次,順便把 .github/workflows/ci.yml 也 commit 進去,確認 Actions 分頁真的有跑起來、綠勾
3. 去 GitHub Settings → Branches 設定 branch protection rule(見第 5.1 節),這個現在設定,之後每個 PR 都會被強制檢查
4. 開始寫 Tickets 功能 → 這一刻起改用 feature/tickets-crud 分支(見第 2 節)
5. 開發完 push、開 PR、加 Copilot review、自己 approve + squash merge
6. 重複 4-5 步驟做下一個功能
```