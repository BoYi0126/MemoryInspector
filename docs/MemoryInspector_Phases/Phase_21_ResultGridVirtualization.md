# Phase 21 - Result Grid Virtualization

## 相依階段

- Phase 11
- Phase 18
- Phase 20

## 目標

建立可顯示大量 Candidate 的結果表格。

## 功能

- Pagination
- Lazy loading
- Sort current page
- Address copy
- Add to Watch
- Save Address
- Read status

## 原則

- 不建立全部 Candidate 的 ViewModel。
- 每頁預設 1000 筆。
- 使用 DataGrid virtualization。
- 切頁時取消前一次載入。

## 驗收標準

- 百萬筆結果時 UI 仍可操作。
- 切頁不造成長時間凍結。
