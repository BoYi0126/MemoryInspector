# Phase 29 - Snapshot Compare

## 相依階段

- Phase 18
- Phase 19

## 目標

比較兩個 Scan Node 或 Memory Snapshot。

## 比較結果

- Added
- Removed
- Changed
- Unchanged
- Count difference
- Storage size difference

## 功能

- Select left / right node
- Summary
- Paged difference view
- Export comparison

## 驗收標準

- 不需同時把兩份完整 Snapshot 載入 RAM。
- 百萬筆資料仍可比較。
