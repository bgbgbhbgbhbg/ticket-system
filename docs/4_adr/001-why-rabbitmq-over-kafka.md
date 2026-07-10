# ADR 001: 為什麼選 RabbitMQ 而不是 Kafka

## 狀態
已採用

## 背景
搶票流程需要「非同步排隊處理訂單」,削峰填谷。候選方案:RabbitMQ、Kafka、Redis Streams。

## 決策
選擇 **RabbitMQ**。

## 理由

| 考量點 | RabbitMQ | Kafka |
|---|---|---|
| 使用場景 | 任務佇列(task queue),訊息處理完即可丟棄 | 事件日誌(event log),需要重播、保留歷史 |
| 本專案需求 | 訂單處理完就結束,不需要重播歷史事件 | 用不到它的核心優勢 |
| 學習曲線 / 本機資源 | 較輕量,單機 container 資源需求低,適合 24GB 筆電開發 | 需要 Zookeeper/KRaft,本機資源消耗較大 |
| dead-letter queue | 原生支援,設計簡單 | 需要額外實作邏輯 |
| Consumer ack 機制 | 明確的 ack/nack,適合「處理失敗要重試」的訂單場景 | 需要自行管理 offset,語意較複雜 |

## 後果
- 犧牲了 Kafka 的高吞吐量與事件重播能力,但本專案不需要「回放歷史訂單事件」這種需求。
- 如果未來要做「訂單事件驅動的其他下游服務(如通知、報表)」,屆時可能需要重新評估是否引入 Kafka 做 event streaming,兩者不衝突,可以並存。

## 面試問題
「我選 RabbitMQ 是因為這個場景是 task queue 語意(處理完即結束),不是 event log 語意(需要保留、重播),Kafka 的優勢在這裡發揮不出來,反而增加本機開發與維運複雜度。」