# Phase 30 - Integration Testing and Performance

## 相依階段

- Phase 04 至 Phase 29

## 目標

完成跨模組測試、壓力測試與資源驗證。

## 測試場景

- Process 在掃描中結束
- Access denied
- 百萬 Candidate
- 多分支 Scan Tree
- 連續 Undo / Branch
- Snapshot 損壞
- Temp 空間不足
- Memory budget 觸發
- Watch 長時間執行
- UI 快速切頁與取消
- Editor feature flag

## 效能指標

- UI 互動延遲
- Peak RAM
- Snapshot write speed
- Snapshot read speed
- Filter throughput
- Temp cleanup time

## 驗收標準

- 不存在已知 Handle / Stream 洩漏。
- 壓力測試 RAM 維持預算內。
- Build 與全部 Tests 通過。
