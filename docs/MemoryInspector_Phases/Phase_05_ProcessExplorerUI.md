# Phase 05 - Process Explorer UI

## 相依階段

- Phase 04

## 目標

建立 Process Explorer WPF 畫面。

## UI 功能

- Refresh
- Search
- PID filter
- Sort
- Auto refresh
- 選取 Process
- 顯示詳細資訊
- Start Monitoring 按鈕

## DataGrid

欄位：

- Name
- PID
- CPU
- Working Set
- Private Memory
- Architecture
- Status

## 要求

- DataGrid virtualization。
- Refresh 不阻塞 UI。
- 自動更新保留選取項。
- Process 消失時清楚標示。

## 驗收標準

- 可掃描、搜尋、排序。
- 大量 Process 下操作流暢。
