# Load Testing Plan

> 針對開發者本機硬體 **MacBook Pro / Apple M5 / 24GB Unified Memory** 調整過的壓測分級。
> 原則:**本機測試數字要真實可信,不要為了數字好看硬測到資源耗盡。**

---

## 1. 為什麼要調整原始的 1000/5000/10000 計畫

24GB 是「統一記憶體」,CPU 和 GPU 共用同一份,而你同時要跑:

- OrbStack 裡的 5 個 container(Postgres、Redis、RabbitMQ、backend、frontend)
- VS Code + GitHub Copilot(常駐吃記憶體)
- 瀏覽器
- k6 本身

如果硬測到 10000 並發,瓶頸很可能來自「筆電記憶體不夠、開始 swap」,而不是系統架構本身的極限。這樣測出來的數字**沒有工程參考價值**,面試時被追問細節反而容易破綻。

---

## 2. 調整後的分級

| 階段 | 並發數 | 目的 | 預期觀察重點 |
|---|---|---|---|
| Baseline | 50 | 確認系統正常運作,建立基準延遲 | p50/p95 latency |
| Normal Load | 300 | 模擬正常流量 | cache hit rate、DB connection pool 使用率 |
| Stress | 1000 | 找出第一個瓶頸 | 觀察是 API、DB、還是 MQ 先撐不住 |
| Breakpoint(選配) | 1500~2000 | 找系統崩潰邊界 | **測到 API 明顯 5xx 增加或 timeout 就停止,不用硬撐到預設數字** |

> 10000 並發那個規格不用在本機做,改成「理論分析」寫在報告裡:根據 1000~2000 的瓶頸曲線,推估更高並發下大概會先在哪個元件崩潰、原因是什麼。這樣講面試官只會覺得你懂資源限制,不會覺得你偷懶。

---

## 3. 保護你電腦的具體作法(不是怕壞掉,是怕跑不動當機要重開機很煩)

1. **k6 不要跑在 Docker/OrbStack 裡**,直接裝在 macOS host 上執行,才不會跟被測系統搶同一份容器資源配額。
2. 每次測試前後用 `docker stats`(OrbStack 也支援)看各 container 的 CPU/memory,超過 80% 就代表這層快要變成假瓶頸。
3. 測試時關掉不必要的 App(瀏覽器分頁、其他 IDE),把資源留給系統本身。
4. 每個階段測完等 30 秒讓系統回穩,再測下一階段,避免疊加效應誤判。
5. 如果真的想看「高並發」的效果,之後可以考慮免費的雲端方案(如 Grafana Cloud k6 或 GitHub Actions runner)去跑更高並發,本機只需要證明「你會設計壓測、看得懂瓶頸」,不需要證明「筆電扛得住 1 萬人」。

---

## 4. k6 安裝與腳本(macOS / Apple Silicon)

### 安裝
```bash
brew install k6
```

### 目錄結構
```
load-tests/
├── baseline.js
├── normal-load.js
└── stress.js
```

### 腳本範例:`load-tests/stress.js`
```javascript
import http from 'k6/http';
import { check, sleep } from 'k6';

export const options = {
  stages: [
    { duration: '30s', target: 100 },   // 暖機
    { duration: '1m', target: 1000 },   // 拉到 1000 並發
    { duration: '2m', target: 1000 },   // 撐住觀察
    { duration: '30s', target: 0 },     // 收尾
  ],
  thresholds: {
    http_req_duration: ['p(95)<500'], // p95 延遲門檻,超過視為降級
    http_req_failed: ['rate<0.05'],   // 錯誤率超過 5% 視為系統開始撐不住
  },
};

const BASE_URL = 'http://localhost:5000/api';

export default function () {
  const res = http.get(`${BASE_URL}/tickets/${__ENV.TICKET_ID}/inventory`);
  check(res, {
    'status is 200': (r) => r.status === 200,
  });
  sleep(1);
}
```

### 執行
```bash
TICKET_ID=<替換成實際票券 id> k6 run load-tests/stress.js
```

**看到 `http_req_failed` threshold 開始 fail 就代表找到瓶頸了,直接停止測試(Ctrl+C),不用等它跑完。**

---

## 5. 判讀結果對照表(面試話術用)

| 觀察現象 | 可能瓶頸 | 對應解法(可以講的方向) |
|---|---|---|
| API p95 latency 上升,但 CPU 使用率不高 | DB connection pool 太小 | 調整 EF Core connection pool size,或加 read replica |
| Redis cache miss rate 突然升高 | Cache TTL 設太短,或 key 設計有問題 | 檢查 `cache-strategy.md` 的 TTL 設定 |
| RabbitMQ queue 長度持續增加不下降 | Consumer(BackgroundService)處理速度跟不上生產速度 | 增加 consumer 數量(scale out worker) |
| DB CPU 飆高 | 樂觀鎖衝突重試次數過多 | 檢查 `domain-state-machine.md` 的重試上限是否合理 |