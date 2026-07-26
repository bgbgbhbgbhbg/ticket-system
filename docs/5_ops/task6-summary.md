# Task 6: Integration Tests — 實作完成總結

## 📋 任務概覽
**日期**: 2026-07-26  
**分支**: `feature/orders-integration-test1`  
**狀態**: ✅ **完成**

---

## 1. 實作範圍

對應 `docs/test-plan.md` 第 2 節（整合測試），完成以下項目：

### 1.1 既有測試（已通過，本次確認無回退）
| 測試編號 | 說明 | 檔案 |
|---|---|---|
| IT-OPT-01 | 兩個並發請求搶同一張 ticket，只有一個成功 | `OptimisticLockTests.cs` |
| IT-OPT-02 | 10 個並發超過庫存，available_quantity 不會扣成負數 | `OptimisticLockTests.cs` |
| IT-OPT-03 | ProcessOrderAsync 成功路徑（庫存扣減、狀態流轉、log 寫入） | `OptimisticLockTests.cs` |
| IT-OPT-04 | 庫存不足時 ProcessOrderAsync 應標記 Failed | `OptimisticLockTests.cs` |

### 1.2 本次新增
| 測試編號 | 說明 | 檔案 |
|---|---|---|
| IT-SCHEMA-01 | available_quantity 的 CHECK constraint 由資料庫層擋住（非應用層） | `SchemaConstraintTests.cs` |
| IT-IDEM-01 | 相同 (user_id, idempotency_key) 第二筆拋 DbUpdateException | `SchemaConstraintTests.cs` |
| IT-SNAP-01 | 票價異動後舊訂單的 total_amount 不隨之改變（快照特性） | `SchemaConstraintTests.cs` |

---

## 2. 測試結果

```
已通過! - 失敗: 0，通過: 8，略過: 0，總計: 8
```

Unit Tests（保持全數通過）：

```
已通過! - 失敗: 0，通過: 30，略過: 0，總計: 30
```

---

## 3. 核心設計決策

### 3.1 Testcontainers 設定
- Image 固定使用 `postgres:18`（需要 `uuidv7()` 原生函式，舊版本不支援）
- 透過 `PostgreSqlFixture` + `[CollectionDefinition]` 讓同 Collection 的所有測試共用一個 Container（不每個 [Fact] 重啟）
- Migration 用 `dbContext.Database.MigrateAsync()` 執行，同時驗證 migration 本身無誤

### 3.2 並發測試設計
- 每個並發 Task 使用獨立的 `AppDbContext` instance（DbContext 非執行緒安全）
- IT-OPT-01 使用固定 `version`（2 個並發競爭同一版本）
- IT-OPT-02 使用動態讀取最新 `version`（10 個並發，最多 2 個成功）

### 3.3 CHECK constraint 測試
- 使用 EF Core `ExecuteSqlAsync(FormattableString)` 繞過應用層，直接向 DB 送 SQL
- `FormattableString` 自動參數化，避免 SQL Injection

### 3.4 idempotency_key 測試
- Constraint 為複合唯一索引 `(user_id, idempotency_key)`，不同 user 可用相同 key
- 透過獨立 `AppDbContext` 發送第二筆，由 EF Core 包裝成 `DbUpdateException`

---

## 4. 同步修正（OrderProcessingWorker.cs）

在 code review 過程中，發現並修正 `OrderProcessingWorker` 的兩個關機路徑問題：

### 4.1 Change 1（不適用）：`DispatchConsumersAsync = true`
- **建議來源**：針對 RabbitMQ.Client v6 的建議
- **判定**：本專案使用 **v7.2.1**，該屬性已在 v7 移除（整個 client 改為 async-first，`AsyncEventingBasicConsumer` 無需額外設定）
- **結論**：不套用，加入會造成編譯失敗

### 4.2 Change 2（已套用）：`HandleMessageAsync` 關機取消處理
- **問題**：`ProcessOrderAsync` 在關機時拋出 `OperationCanceledException`，被 `catch (Exception)` 吃掉，誤觸 republish + ack，導致正常關機時多送一份訊息
- **修正**：在 `NpgsqlException` catch 之前加入 `catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)`，不 ack/nack，讓 channel 關閉時 RabbitMQ 自動 requeue

### 4.3 Change 3（已套用）：`ExecuteAsync` 重連等待取消處理
- **問題**：重連 delay 用 `await Task.Delay(..., stoppingToken)` 在 `catch (Exception)` 區塊中；關機時 delay 拋出 `OperationCanceledException`，無法被外層 `catch (OperationCanceledException)` 捕捉，導致 BackgroundService 以例外結束而非正常退出
- **修正**：在 `catch (Exception)` 內對 `Task.Delay` 加 try-catch，取消時直接 `break`

---

## 5. 前端修正

- **問題**：`auth-context.tsx` 的 fallback API port 為 `5000`，而 `api.ts` 及後端實際 port 為 `5263`
- **修正**：`auth-context.tsx` 的 `API_BASE_URL` fallback 從 `5000` 改為 `5263`

---

## 6. Commit 訊息

```
test(infra): 新增 Testcontainers 整合測試，驗證 CHECK constraint、idempotency key 唯一性、total_amount 快照特性

參照 test-plan.md IT-SCHEMA-01、IT-IDEM-01、IT-SNAP-01
- SchemaConstraintTests.cs 新增 3 個測試案例
- 所有整合測試：8 通過 / 0 失敗

fix(infra): OrderProcessingWorker 正常關機路徑修正

- HandleMessageAsync：正常關機時不做 ack/nack，避免多送訊息
- ExecuteAsync：重連等待 Task.Delay 加 try-catch，確保 BackgroundService 正常退出

fix(web): auth-context API base URL fallback port 5000 → 5263
```
