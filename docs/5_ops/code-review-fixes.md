# 程式碼審核修改紀錄

**審核日期**：2026-07-12  
**目標分支**：`feature/orders-worker`  
**修改後測試結果**：✅ 全部 35 個測試通過（Unit 30 + Integration 5，0 failures）

---

## 問題 1 ✅ — 連線中斷導致 Worker 永久卡死

**檔案**：`TicketBooking.Infrastructure/Messaging/OrderProcessingWorker.cs`

**問題**：`ExecuteAsync` 使用 `Task.Delay(Timeout.Infinite, stoppingToken)` 保持 channel 存活，但此 delay 只感知 `stoppingToken`，不感知 RabbitMQ 連線狀態。若 broker 在服務關機之前斷線，delay 永遠不結束，`catch` 區塊無法觸發，Worker 永久阻塞，無法重連。

**修改**：  
建立 `connectionCts`（`CancellationTokenSource.CreateLinkedTokenSource(stoppingToken)`），並訂閱 `connection.ConnectionShutdownAsync` 事件，在連線中斷時取消 `connectionCts`。`Task.Delay` 改用 `connectionCts.Token`，確保連線斷時拋出 `OperationCanceledException`，進入重連流程。

```csharp
using var connectionCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
connection.ConnectionShutdownAsync += (_, _) =>
{
    _logger.LogWarning("RabbitMQ 連線中斷，觸發重連");
    connectionCts.Cancel();
    return Task.CompletedTask;
};
await Task.Delay(Timeout.Infinite, connectionCts.Token);
```

---

## 問題 2 ✅ — `nack(requeue:true)` 永遠不進 DLQ，可能無限重試

**檔案**：`TicketBooking.Infrastructure/Messaging/OrderProcessingWorker.cs`

**問題**：規格（`message-contracts.md §4`）要求「重新投遞達 3 次仍失敗 → 進 DLQ」。但技術性失敗的 catch 一律使用 `nack(requeue:true)`，RabbitMQ 的 `x-dead-letter-exchange` 設定只在 `nack(requeue:false)` 時才生效，導致訊息可能無限重試，永遠進不了 DLQ。

**修改**：  
利用 `ea.Redelivered` 判斷是否已重試過：首次投遞失敗使用 `requeue:true`（再給一次機會）；若訊息已被重新投遞（`Redelivered=true`）且再次失敗，則改用 `requeue:false` 送往 DLQ，阻止無限循環。

```csharp
var requeue = !ea.Redelivered;
await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: requeue);
```

> **注意**：`ea.Redelivered` 為布林值，僅能提供「已重試至少 1 次」的判斷，最多給出 2 次投遞機會。若要精確實作規格要求的「3 次重試」，需改用 Quorum Queue 搭配 `x-delivery-limit: 3`，或實作 retry exchange + x-death header 計數的延遲重試方案，已記錄為技術債。

---

## 問題 3 ✅ — 重投遞訊息導致 `InvalidStatusTransitionException` 無限 nack

**檔案**：`TicketBooking.Application/Services/OrderProcessingService.cs`

**問題**：`ProcessOrderAsync` 每次都嘗試執行 `Pending → Processing` 轉換。若 Worker nack 後訊息被重新投遞，而此時訂單已是 `Processing`（或 `Success`/`Failed`），`Order.TransitionTo(Processing)` 會拋出 `InvalidStatusTransitionException`，被外層 catch 捕捉並再度 nack，形成無限循環。

**修改**：  
在 `GetByIdAsync` 之後、執行轉換之前，加入冪等性保護邏輯：

- 若訂單已是 `Success` 或 `Failed`（終態）→ 直接 return，讓 Worker ack 丟棄。
- 若訂單已是 `Processing`（重投遞情境）→ 跳過 `Pending→Processing` 轉換，繼續執行庫存扣減迴圈。
- 若訂單是 `Pending` → 正常執行轉換（原始流程）。

---

## 問題 4 ✅ — UT-PROC-08 `Arg.Do` 用法錯誤，未驗證 `FromStatus`

**檔案**：`tests/TicketBooking.UnitTests/Services/OrderProcessingServiceTests.cs`

**問題**：原始測試直接 `await` 呼叫 substitute 方法搭配 `Arg.Do`，這不是 NSubstitute 正確的捕捉寫法，`Arg.Do` 只有在 `.Returns()` 或 `.When().Do()` 的設定情境中才有效；此外，Assert 段落完全沒有驗證 `FromStatus`，測試的核心目的（確認 `FromStatus == Processing`）從未被驗證。

**修改**：  
改用 `_orderRepository.When(...).Do(callInfo => capturedFailLog = callInfo.Arg<OrderStatusLog>())` 正確捕捉每次呼叫；並新增 `Assert.Equal(OrderStatus.Processing, capturedFailLog.FromStatus)` 斷言，真正驗證測試意圖。

---

## 問題 5 ✅ — IT-OPT-01 誤導性註解

**檔案**：`tests/TicketBooking.IntegrationTests/OptimisticLockTests.cs`

**問題**：IT-OPT-01 的 Act 區塊有一行「使用同一個 DbContext scope，模擬 race condition」，但實際程式碼為每個 task 建立獨立的 DbContext，兩者矛盾，會誤導後續維護者。

**修改**：  
移除誤導性說明，保留正確的「使用獨立的 DbContext 避免 EF Core change tracking 干擾並發讀取」說明。

---

## 問題 6 ✅ — xUnit 每個測試方法重新啟動 PostgreSQL Container（效能問題）

**檔案**：新增 `tests/TicketBooking.IntegrationTests/PostgreSqlFixture.cs`，修改 `OptimisticLockTests.cs`

**問題**：xUnit 為每個測試方法建立新的測試類別實例。原始程式碼將 `PostgreSqlContainer` 宣告為實例欄位，導致每個測試方法都啟動/停止一個獨立的 PostgreSQL Container，嚴重拖慢測試速度，且在 CI 環境中容易因資源競爭導致 flaky test。雖然已有 `[Collection("OptimisticLock")]` 屬性，但沒有搭配 `ICollectionFixture`，因此沒有發揮共享 Container 的效果。

**修改**：  
1. 新增 `PostgreSqlFixture` 類別（實作 `IAsyncLifetime`），負責管理 Container 生命週期，並在 `InitializeAsync` 執行一次 Migration。
2. 新增 `[CollectionDefinition("OptimisticLock")]` 定義 class，搭配 `ICollectionFixture<PostgreSqlFixture>`。
3. `OptimisticLockTests` 改為透過建構子注入 `PostgreSqlFixture`，`InitializeAsync` 僅建立每個測試專用的 `DbContext`，不再啟動 Container 或執行 Migration。

**效果**：整個 Collection 的 5 個測試共用一個 Container（啟動一次），而非每個測試啟動/停止一次。

---

## 問題 7 ✅ — `JsonException` 流入 generic catch 形成毒訊息無限循環

**檔案**：`TicketBooking.Infrastructure/Messaging/OrderProcessingWorker.cs`

**問題**：`HandleMessageAsync` 的 try block 包含 `JsonSerializer.Deserialize`，若收到格式錯誤的訊息，會拋出 `JsonException`。此例外被 `catch (Exception)` 捕捉並以 `nack(requeue:true)` 處理，導致格式錯誤的訊息（poison message）被無限重新投遞。

**修改**：  
在 `catch (NpgsqlException)` 之前加入 `catch (JsonException)`，對格式錯誤的訊息執行 `BasicAckAsync`（ack 丟棄），阻止毒訊息無限循環。

---

## 問題 8 ✅ — IT-OPT-02 全部 task 使用同版本，僅能驗 1 筆成功

**檔案**：`tests/TicketBooking.IntegrationTests/OptimisticLockTests.cs`

**問題**：IT-OPT-02 意圖驗證「10 人同時搶購只剩 2 張的票，庫存不會被扣成負數」。但所有 task 都使用相同的 `initialVersion` 執行 CAS，由於 CAS 只允許 version 匹配的更新成功，10 個 task 中最多只有 1 個可以成功（不是 2 個）。`successCount <= 2` 的斷言對這個場景是恆真命題，無法真正驗證「最多成功 2 筆」的情境。

**修改**：  
每個 task 改為先讀取當前最新的 ticket（`AsNoTracking`），使用讀到的 version 執行 CAS，模擬真實的「讀-改-寫」應用程式行為。並新增 `Assert.Equal(2 - successCount, updatedTicket.AvailableQuantity)` 確保 CAS 成功次數與剩餘庫存完全一致，精確驗證沒有超賣。

---

## 變更的檔案清單

| 檔案 | 異動類型 | 對應修改 |
|---|---|---|
| `TicketBooking.Infrastructure/Messaging/OrderProcessingWorker.cs` | 修改 | Fix 1, 2, 7 |
| `TicketBooking.Application/Services/OrderProcessingService.cs` | 修改 | Fix 3 |
| `tests/TicketBooking.UnitTests/Services/OrderProcessingServiceTests.cs` | 修改 | Fix 4 |
| `tests/TicketBooking.IntegrationTests/PostgreSqlFixture.cs` | **新增** | Fix 6 |
| `tests/TicketBooking.IntegrationTests/OptimisticLockTests.cs` | 修改 | Fix 5, 6, 8 |

---

## 建議 Commit 訊息

```
fix(application): ProcessOrderAsync 加入冪等性保護，處理重投遞時 Processing 狀態
fix(infra): OrderProcessingWorker 修正連線中斷偵測、DLQ 路由及毒訊息處理
test(unit): UT-PROC-08 修正 NSubstitute Arg.Do 用法並補充 FromStatus 驗證
test(integration): OptimisticLockTests 改用 ICollectionFixture 共享 Container
test(integration): IT-OPT-02 改為每個 task 獨立讀取 version，正確驗證庫存上限場景
```

---

## 尚未處理的技術債

- 問題 2 的「精確 3 次重試」：目前實作僅能提供 2 次投遞機會（`ea.Redelivered` 為布林值）。完整支援規格的「3 次重試後進 DLQ」需改為 Quorum Queue（搭配 `x-delivery-limit: 3`）或實作 retry exchange + x-death 計數，建議在未來的基礎設施改善任務中處理。
