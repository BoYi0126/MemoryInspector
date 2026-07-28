# Phase 09 - Memory Reader Core

## 相依階段

- Phase 06
- Phase 07

## 目標

建立唯讀 Memory Reader 抽象與 Windows 實作。

## 介面

- Read block
- Try read typed value
- Batch read
- Partial read result
- CancellationToken

## 要求

- 不讓 UI 直接操作 Handle。
- 失效位址回傳 Result。
- Chunk read 可調整大小。
- 支援批次讀取降低呼叫成本。

## 驗收標準

- 可讀取測試 Process 的指定區域。
- 部分讀取不造成崩潰。
- Handle 正確釋放。
