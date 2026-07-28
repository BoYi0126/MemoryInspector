# Phase 10 - Scanner Foundation and Value Parsing

## 相依階段

- Phase 02
- Phase 09

## 目標

建立 Scanner 基礎模型、數值解析與比對介面。

## 型別

- Byte
- Int16 / UInt16
- Int32 / UInt32
- Int64 / UInt64
- Float
- Double

## 模型

- `ScanValueType`
- `ScanComparisonMode`
- `ScanRequest`
- `ScanResult`
- `CandidateAddress`
- `IScanValueParser`
- `IValueMatcher`

## 要求

- 整數範圍驗證。
- Float / Double tolerance。
- NaN / Infinity 明確處理。
- Aligned / Unaligned 模式。

## 驗收標準

- 所有型別解析有測試。
- Invalid input 不會開始掃描。
