# Phase 28 - Hex Viewer

## 相依階段

- Phase 09
- Phase 21

## 目標

建立虛擬化 Hex Viewer。

## 功能

- Address
- Offset
- Hex bytes
- ASCII
- Jump to address
- Search bytes
- Page navigation
- Refresh

## 原則

- 不一次讀取巨大 Region。
- 採固定大小 Window。
- 讀取失敗區域清楚標示。
- 預設唯讀。

## 驗收標準

- 可從 Region 或 Scan Result 跳轉。
- 大型 Region 操作流暢。
