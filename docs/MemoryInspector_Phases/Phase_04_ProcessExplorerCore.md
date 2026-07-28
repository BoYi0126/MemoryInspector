# Phase 04 - Process Explorer Core

## 相依階段

- Phase 02
- Phase 03

## 目標

建立系統 Process 列舉與摘要資訊服務。

## 顯示欄位

- Process Name
- PID
- CPU Usage
- Working Set
- Private Memory
- Virtual Memory
- Architecture
- Start Time
- Executable Path
- Status

## 服務

- `ISystemProcessService`
- `ProcessSummary`
- `ProcessArchitecture`
- `ProcessAccessStatus`

## 穩定性要求

- 單一 Process Access Denied 不得中止全部列舉。
- Process 在列舉中結束不得造成崩潰。
- 不長期持有全部 Process Handle。
- 支援 CancellationToken。

## 測試

- 空清單。
- Process 結束。
- 權限不足欄位。
- 記憶體格式化。

## 驗收標準

- 可取得目前 Process 清單。
- 每筆失敗皆獨立處理。
