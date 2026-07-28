# Phase 26 - Memory Editor UI

## 相依階段

- Phase 21：Result Grid Virtualization
- Phase 22：Watch Window
- Phase 23：Saved Address JSON
- Phase 24：Memory Editor Foundation
- Phase 25：Windows Memory Writer

## 目標

完成 Memory Editor 的 WPF 操作介面，讓使用者可以從 Scan Result、Watch Window、Saved Address 或手動 Address 執行經確認的單次寫入。

## 入口

支援：

- Scan Result → Edit Value
- Watch Window → Edit Value
- Saved Address → Edit Value
- Manual Address → Edit Value（需設定允許）
- Hex Viewer → Edit Value（Phase 30 完成後再串接）

## Editor Panel

顯示：

- Target Process
- PID
- Session status
- Address
- Memory Region summary
- Value Type
- Current Value
- Current Bytes
- New Value
- New Bytes preview
- Input format
- Verify After Write
- Compare Before Write
- User Note

## 輸入格式

- Decimal
- Hexadecimal

十六進位輸入必須明確顯示：

- 解析後數值
- 實際 byte order
- 寫入 byte count

## Confirmation Dialog

按下 Write 後顯示最終確認：

```text
Process
Address
Type
Original Value
New Value
Original Bytes
New Bytes
Verify After Write
```

使用者確認後才呼叫 Application Service。

若設定 `requireConfirmation = true`，不得跳過。

## 寫入結果

顯示：

- Success / Failure
- Written bytes
- Original value
- Requested value
- Read-back value
- Verification status
- Error reason
- Audit timestamp

寫入成功後：

- Refresh Watch item
- Refresh current Scan Result row
- Refresh Saved Address current value
- 不自動重新執行整輪 Scan

## Undo Last Write

第一版支援有限度的手動 Undo：

- 只保存本次 App Session 的最近成功寫入。
- Undo 會把 Original Bytes 當成新的寫入請求。
- Undo 前仍須重新讀取與確認。
- 若目前值已不是上次寫入值，顯示衝突並要求確認。
- Undo 也必須建立 Audit Entry。

Undo 不保證能恢復目標程式的邏輯狀態，只能嘗試恢復原始 bytes。

## Write History

顯示：

- Time
- Process
- Address
- Type
- Original
- Requested
- Read-back
- Result
- Source
- Note

功能：

- Filter
- Copy
- Export audit summary
- Retry failed request（重新確認後）
- Undo eligible write

## 狀態管理

Write 按鈕只在下列條件成立時啟用：

- Feature enabled
- Monitoring Session active
- Target alive
- Input valid
- Address valid
- Region writable
- No write operation running

## 錯誤體驗

不可只顯示一般的「寫入失敗」。

UI 需根據錯誤分類顯示，例如：

- Target 已結束
- Address 不在有效 Region
- Region 不可寫
- 原值已變更
- 寫入不完整
- 寫入後驗證失敗
- 權限不足
- Session 已失效

## UI 執行要求

- 使用 async command。
- 支援 CancellationToken。
- 寫入中禁止重複送出。
- 不阻塞 Dispatcher。
- 不在 ViewModel 持有 Process Handle。
- 稽核寫入失敗也不得使 UI 崩潰。

## 本階段不實作

- Freeze Value
- 自動循環寫入
- 批次修改
- 腳本
- Hotkey 自動寫入
- Protection override
- Injection / Hook

## 驗收標準

- 可從 Scan Result 開啟 Editor。
- 可從 Watch Window 開啟 Editor。
- 可從 Saved Address 開啟 Editor。
- 可輸入 Decimal / Hexadecimal。
- 寫入前顯示完整確認。
- 對 Test Target 寫入後 UI 顯示讀回結果。
- Undo 流程有衝突檢查。
- Write History 可檢視。
- Feature 關閉時所有入口不可寫入。
- Build 與 UI Tests 通過。
