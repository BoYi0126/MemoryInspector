# Phase 20 - LRU Cache and Memory Budget

## 相依階段

- Phase 18
- Phase 19

## 目標

限制 RAM 使用量，避免 Scan Tree 導致記憶體暴增。

## 預設值

- Max Candidate Memory: 512 MB
- Max Cached Nodes: 3
- Page Size: 1000
- Memory-only Threshold: 100,000
- Disk-backed Threshold: 1,000,000

## 行為

超過 Memory Budget：

1. 選擇最久未使用 Node。
2. Flush 至磁碟。
3. 釋放 Candidate Buffer。
4. 保留 Metadata 與 Index。

## 要求

- 可顯示目前 RAM / Disk 使用量。
- 允許使用者調整預算。
- 預算降低時立即觸發 eviction。

## 驗收標準

- 多分支操作下 RAM 維持預算內。
- 不產生 OutOfMemoryException。
