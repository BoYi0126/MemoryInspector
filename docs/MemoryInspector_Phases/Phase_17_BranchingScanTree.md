# Phase 17 - Branching Scan Tree

## 相依階段

- Phase 16
- Phase 18

## 目標

將線性歷史升級成可回溯、可分支的 Scan Tree。

## 功能

- Branch From Here
- Set Active Node
- Rename Node
- Pin Node
- Delete Branch
- Compare Nodes
- Tree navigation

## 節點欄位

- Node ID
- Parent ID
- Children
- Filter Mode
- Counts
- Duration
- Storage Type
- Storage Path
- IsPinned
- IsActive

## 規則

- 建立分支不得複製完整 Candidate 至 RAM。
- Pin 只代表保留 Snapshot，不代表常駐 RAM。
- Active Node 唯一。

## 驗收標準

- 可從歷史節點建立新分支。
- 切換節點後可繼續篩選。
