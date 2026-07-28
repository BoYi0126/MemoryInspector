# Phase 06 - Monitoring Session

## 相依階段

- Phase 04
- Phase 05

## 目標

建立單一目標 Process 的 Monitoring Session。

## Session Identity

必須包含：

- PID
- Process Start Time
- Architecture
- Process Name

不可只使用 PID。

## 狀態

- Disconnected
- Connecting
- Connected
- AccessDenied
- TargetExited
- Invalidated
- Error

## 功能

- Start Monitoring
- Stop Monitoring
- Target liveness check
- Session invalidation
- Resource disposal

## 驗收標準

- 同時間只允許一個 Active Session。
- Process 結束後自動失效。
- Stop 後釋放所有資源。
