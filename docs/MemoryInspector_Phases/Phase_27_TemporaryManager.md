# Phase 27 - Temporary Manager

## 相依階段

- Phase 18
- Phase 19
- Phase 20

## 目標

建立 Session / Snapshot 暫存管理。

## 功能

- Delete Current Node Temp
- Delete Branch Temp
- Delete Current Session
- Delete All Temp
- Auto Cleanup
- Open Temp Folder
- Compact Session
- Temp statistics

## 規則

- 刪除前關閉所有 Stream。
- 掃描中不可直接刪除。
- Pinned Snapshot 預設保留。
- Reference Count = 0 才可刪除。
- 啟動時清理不完整 `.tmp`。

## 驗收標準

- 刪除 Session 不影響 Saved Address。
- Compact 後 Tree 仍可讀取。
