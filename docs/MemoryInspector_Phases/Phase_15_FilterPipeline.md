# Phase 15 - Filter Pipeline

## 相依階段

- Phase 13
- Phase 14

## 目標

建立可重複執行的多輪篩選流程。

## 功能

- Keep Result
- Discard Result
- Continue Filtering
- Current Candidate Count
- Active Round
- Filter summary

## 規則

- 每輪結果先進入 Pending 狀態。
- Keep 後才成為下一輪輸入。
- Discard 後回到 Parent。
- Pipeline 輪數不設硬限制。
- 實際容量由 Storage Policy 控制。

## 驗收標準

- 可依序執行 Unchanged → Changed → Increased。
- 每輪 Before / After Count 正確。
