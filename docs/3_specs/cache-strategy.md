# Cache Strategy Specification

> 定義 Redis 在本系統中的 Key 命名、TTL、失效(invalidation)規則。對應 `adr-002-cache-aside-vs-write-through.md` 的決策。

---

## 1. Cache Key 命名規則

統一格式:`{domain}:{id}:{sub-resource}`

| Key 格式 | 範例 | 說明 |
|---|---|---|
| `ticket:{ticketId}` | `ticket:3f2e...` | 票券詳情(name, eventName, price 等靜態欄位) |
| `ticket:{ticketId}:inventory` | `ticket:3f2e...:inventory` | 剩餘庫存(`available_quantity`),高頻異動 |
| `user:{userId}:profile` | `user:9a1b...:profile` | 使用者基本資料(選配,登入頻率不高,加不加皆可) |

---

## 2. TTL 與失效策略

| Key 類型 | TTL | 失效方式 |
|---|---|---|
| `ticket:{ticketId}` | 5 分鐘 | 自然過期即可,票券詳情(名稱/價格)異動頻率低 |
| `ticket:{ticketId}:inventory` | **不依賴 TTL** | 訂單處理完成(無論 Success 或 Failed)後,BackgroundService **主動呼叫 DEL** 該 key,下次讀取自然從 DB 重新載入最新庫存 |
| `user:{userId}:profile` | 10 分鐘 | 自然過期 |

**為什麼庫存不能只靠 TTL 自然過期**:搶票尖峰時,如果庫存 cache 還沒過期但 DB 庫存已經被扣減,使用者會看到過期的「還有票」訊息,造成明明沒票卻讓使用者以為搶得到的體驗問題(雖然真正下單時 DB 樂觀鎖會擋下,但體驗上會造成困惑與客訴),所以庫存類 key 用主動 invalidate,不能偷懶只設 TTL。

---

## 3. 讀取流程(Cache-Aside)

```
API 收到查詢請求
    ↓
GET ticket:{id}:inventory
    ↓
命中(cache hit)？
    ├─ 是 → 直接回傳
    └─ 否 → 查詢 PostgreSQL
              ↓
           SET ticket:{id}:inventory (value, TTL 依上表)
              ↓
           回傳結果
```

---

## 4. 寫入流程(訂單處理完成後)

```
BackgroundService 完成訂單狀態轉換(Success 或 Failed)
    ↓
DEL ticket:{ticketId}:inventory
    ↓
(不主動 SET 新值，讓下一次讀取請求自然觸發 cache-aside 重新載入，
 避免 worker 短時間內處理大量訂單時，重複寫入 cache 造成額外負擔)
```

---

## 5. Redis 故障時的降級行為

若 Redis 連線失敗:
- 讀取路徑:catch 例外後直接查 DB,回傳結果,**不讓 Redis 故障影響 API 可用性**(只是延遲變高)
- 寫入路徑(DEL):catch 例外後只記 log,不阻擋訂單狀態轉換的主流程(cache 只是效能優化,不應該因為它掛掉導致核心業務邏輯失敗)

對應 `/health` endpoint(見 `specs/api-spec.yaml`):Redis 若異常回報為 `Degraded` 而非 `Unhealthy`,因為系統仍可運作只是變慢。

---

## 6. 與 k6 壓測的對應

`ops/load-testing-plan.md` 裡 `/tickets/{ticketId}/inventory` 回應的 `cacheHit` 欄位,就是用來在壓測時觀察 cache hit rate 是否符合預期(高並發下 hit rate 應該很高,如果偏低代表 TTL 或 invalidate 邏輯可能有問題)。