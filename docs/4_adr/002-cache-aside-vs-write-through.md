# ADR 002: Cache-Aside vs Write-Through

## 狀態
已採用

## 背景
Redis 用於快取票券庫存與詳情,減少 DB 讀取壓力。需要決定快取更新策略。

## 決策
採用 **Cache-Aside(Lazy Loading)**,不用 Write-Through。

## 理由

| 策略 | 說明 | 是否採用 |
|---|---|---|
| Cache-Aside | 讀取先查 cache,miss 才查 DB 並寫回 cache;寫入時直接更新 DB,並主動刪除(invalidate)對應 cache key | ✅ 採用 |
| Write-Through | 每次寫入 DB 的同時同步寫入 cache | ❌ 不採用 |

Cache-Aside 的優勢在本專案場景下更合適:
1. **讀多寫少**:查詢票券資訊的頻率遠高於票券資料異動的頻率,Cache-Aside 對讀取路徑的優化更直接。
2. **實作簡單**:不需要保證「DB 寫入」與「cache 寫入」的雙寫一致性,只要寫入 DB 成功後 invalidate 對應 key 即可,下一次讀取自然會重新載入最新資料。
3. **容錯性**:即使 Redis 整個掛掉,系統仍可退化成直接查 DB(效能下降但不會整個系統掛掉),Write-Through 若雙寫失敗需要更複雜的補償機制。

## 具體規則(對應 `specs/cache-strategy.md`)
- Key 格式:`ticket:{ticketId}`,`ticket:{ticketId}:inventory`
- TTL:詳情類資料 5 分鐘;`available_quantity` 這種高頻異動資料改用**主動 invalidate**(訂單處理完成後立即刪除該 key),不依賴 TTL 自然過期,避免搶票尖峰時讀到過期庫存誤導使用者。

## 後果
- 每次庫存異動都要記得呼叫 invalidate,如果哪個程式路徑忘記呼叫,會產生短暫的 cache 不一致(stale read)。這是可接受的 trade-off,因為 DB 的樂觀鎖機制(見 ADR 003)才是防超賣的真正防線,cache 只是輔助讀取效能,**不是資料一致性的來源**。

## 技術討論重點
「Cache 在這個系統裡的角色是『降低讀取延遲』,不是『資料正確性的保證者』——真正防超賣是靠 DB 樂觀鎖,這個分工要講清楚,不然會讓人以為你以為 cache 可以保證強一致性。」