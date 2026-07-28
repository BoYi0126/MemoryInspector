# Phase 02 - Common Models and Result Pattern

## 相依階段

- Phase 01

## 目標

建立統一的成功、失敗與錯誤傳遞方式。

## 實作內容

- `Result`
- `Result<T>`
- `Error`
- `ErrorCode`
- `Guard`
- `PagedResult<T>`
- `OperationProgress`
- `ByteSizeFormatter`
- `HexAddressFormatter`

## 要求

- UI 可將 Error 轉為可理解訊息。
- 不使用空的 catch。
- 可保留原始例外供日誌使用。
- 領域層不依賴 MessageBox。

## 測試

- Result 成功與失敗。
- Error chaining。
- Byte 格式化。
- Hex 位址格式化。
- Pagination 邊界。

## 驗收標準

- Core 與 Application 統一使用 Result Pattern。
- 所有基礎工具具有單元測試。
