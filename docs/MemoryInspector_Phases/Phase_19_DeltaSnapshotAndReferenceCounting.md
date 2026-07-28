# Phase 19 - Delta Snapshot and Reference Counting

## 相依階段

- Phase 17
- Phase 18

## 目標

避免每個 Scan Tree Node 都重複保存完整 Candidate。

## Delta 類型

- DeltaKeep
- DeltaRemove

系統選擇較小的表示方式。

## 規則

- Delta 依賴 Parent Snapshot。
- Snapshot 保存 Reference Count。
- 刪除 Branch 時只刪除無參考檔案。
- Delta Chain 不可無限延長。

## Full Snapshot 策略

- 每 5 個節點建立一次 Full Snapshot。
- 或 Delta 累積容量超過 Full Snapshot 50%。

## 驗收標準

- 分支共用 Parent 時不會誤刪檔案。
- 長 Delta Chain 可自動 compact。
