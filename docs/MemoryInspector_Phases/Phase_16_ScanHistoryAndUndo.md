# Phase 16 - Scan History and Undo

## 相依階段

- Phase 15

## 目標

建立線性歷史、Undo 與 Scan Round Metadata。

## 每輪保存

- Round ID
- Parent ID
- Mode
- Duration
- Input
- Before Count
- After Count
- Created Time
- Storage Reference

## 功能

- Undo last filter
- Redo pending filter
- Rename round
- Delete pending round

## 驗收標準

- Undo 可回到上一輪。
- Undo 不重新掃描 Process。
- 歷史資料重啟後可讀取。
