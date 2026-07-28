# Phase 24 - Memory Editor Foundation

## 相依階段

- Phase 02：Common Models and Result Pattern
- Phase 06：Monitoring Session
- Phase 09：Memory Reader Core
- Phase 10：Scanner Foundation and Value Parsing
- Phase 22：Watch Window
- Phase 23：Saved Address JSON

## 目標

建立 Memory Editor 的領域模型、驗證流程、功能開關、寫入稽核與測試替身。

本階段先完成「寫入功能的架構與安全邊界」，不在此階段直接呼叫 Windows 寫入 API。

## 使用範圍

僅用於：

- 使用者自行開發的程式
- 測試程式
- 已取得明確授權的目標 Process
- 除錯與驗證用途

不包含：

- 權限繞過
- 程序注入
- Hook
- 核心驅動
- 防護或反偵測規避
- 自動修改記憶體保護屬性

## 核心模型

建立：

- `MemoryWriteRequest`
- `MemoryWriteResult`
- `MemoryWriteVerificationResult`
- `MemoryWriteAuditEntry`
- `MemoryWriteSource`
- `MemoryWriteFailureReason`
- `MemoryEditorSettings`
- `MemoryEditorFeatureState`

### MemoryWriteRequest 建議欄位

- Session ID
- Target Process Identity
- Address
- Value Type
- Input Text
- Parsed Bytes
- Expected Original Value（可選）
- Verify After Write
- Source
- User Note
- Created Time

### MemoryWriteResult 建議欄位

- Success
- Address
- Requested Byte Count
- Written Byte Count
- Original Value
- Requested Value
- Read-back Value
- Verification Status
- Failure Reason
- Error
- Completed Time

## 寫入來源

`MemoryWriteSource` 至少支援：

- Scan Result
- Watch Window
- Saved Address
- Manual Address
- Hex Viewer（後續）

## 數值序列化

建立：

- `IMemoryValueSerializer`
- `MemoryValueSerializer`

支援：

- Byte
- Int16 / UInt16
- Int32 / UInt32
- Int64 / UInt64
- Float
- Double

要求：

- 使用目標平台位元序。
- 驗證輸入範圍。
- Float / Double 支援 NaN 與 Infinity 的明確策略。
- 顯示十進位與十六進位預覽。
- 不允許無法完整解析的輸入進入寫入流程。

## 功能開關

Memory Editor 預設停用。

設定範例：

```json
{
  "memoryEditor": {
    "enabled": false,
    "requireConfirmation": true,
    "verifyAfterWrite": true,
    "allowManualAddress": false
  }
}
```

啟用時必須顯示：

- 功能用途
- 風險提醒
- 僅限自行開發或授權程式的聲明
- 啟用時間

## 寫入確認模型

建立確認資料：

- Process Name
- PID
- Process Start Time
- Address
- Region
- Value Type
- Original Value
- New Value
- Original Bytes
- New Bytes
- Verify After Write

確認 Dialog 實際 UI 於 Phase 26 完成。

## Audit Log

每一次嘗試都必須記錄：

- Target identity
- Address
- Type
- Original value
- Requested value
- Success / Failure
- Verification result
- Error code
- Timestamp
- Source

稽核紀錄與一般 App Log 分離。

禁止記錄不必要的大型 Memory Dump。

## 介面

建立：

- `IMemoryWriter`
- `IMemoryWriteService`
- `IMemoryWriteAuditService`
- `IMemoryEditorFeatureService`

`IMemoryWriter` 在本階段提供：

- Mock implementation
- Denied implementation
- No-op implementation for tests

## 測試

- 各數值型別序列化。
- Overflow / invalid input。
- Feature disabled。
- Expected original value mismatch。
- Audit success / failure。
- Process identity mismatch。
- Mock write verification。

## 驗收標準

- Memory Editor 預設關閉。
- 未啟用時所有寫入請求明確失敗。
- 所有輸入可在寫入前轉換成確定的 byte sequence。
- 所有嘗試都建立 Audit Entry。
- Core 與 Application 不依賴 Windows Native API。
- Build 與 Tests 通過。

## 不在本階段處理

- Windows 實際跨 Process 寫入。
- WPF Memory Editor 完整畫面。
- Freeze / Value Lock。
- 批次寫入。
- Memory Protection 修改。
