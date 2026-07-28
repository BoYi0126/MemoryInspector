# Phase 25 - Windows Memory Writer

## 相依階段

- Phase 06：Monitoring Session
- Phase 07：Windows Memory Region Provider
- Phase 09：Memory Reader Core
- Phase 24：Memory Editor Foundation

## 目標

在 `MemoryInspector.Windows` 中完成 Windows 平台的單次記憶體寫入 Adapter，並整合寫入前檢查及寫入後驗證。

本階段只支援自行開發或已授權目標 Process 的正常使用者模式寫入。

## 平台實作

建立：

- `WindowsMemoryWriter`
- `WindowsProcessWriteHandle`
- `MemoryWriteRegionValidator`
- `MemoryWriteVerificationService`

平台實作應封裝於 `MemoryInspector.Windows`。

WPF、Core 與 Application 不得直接宣告或呼叫 Native API。

## 支援型別

- Byte
- Int16 / UInt16
- Int32 / UInt32
- Int64 / UInt64
- Float
- Double

所有型別先由 Phase 24 的 Serializer 轉成 bytes，再交由 Writer 處理。

## 寫入流程

```text
Validate feature
→ Validate session identity
→ Validate target is alive
→ Locate memory region
→ Validate requested range
→ Validate region is writable
→ Read original value
→ Optional expected-value check
→ Perform one write
→ Read back
→ Verify
→ Create audit entry
→ Release resources
```

## Session 驗證

不可只使用 PID。

寫入前確認：

- PID
- Process Start Time
- Process Name
- Architecture
- Active Monitoring Session ID

若 PID 已被其他 Process 重用，必須拒絕寫入。

## Region 驗證

寫入前確認：

- Address 位於已知 Region。
- `Address + Length` 不越過 Region 邊界。
- Region 為 committed。
- Region 具備可寫屬性。
- Region 不是 Guard / NoAccess。
- 寫入範圍沒有 UInt64 overflow。

本專案不自動變更 Region protection。

## Original Value Check

支援可選的 Compare-Before-Write：

```text
Expected Original Value
```

若目前值與預期原值不同：

- 取消寫入。
- 回傳 `OriginalValueMismatch`。
- 在 UI 顯示資料可能已更新。

此功能降低在值已改變時覆寫錯誤資料的風險。

## 寫入後驗證

預設啟用：

1. 完成單次寫入。
2. 立即重新讀取相同長度。
3. 比較實際 bytes 與 requested bytes。
4. 回傳 Verified / Mismatch / ReadFailed。

寫入成功但驗證失敗，結果不可標記為完整成功。

## 錯誤分類

至少包含：

- FeatureDisabled
- SessionInvalid
- TargetExited
- AccessDenied
- InvalidAddress
- RegionNotFound
- RegionNotCommitted
- RegionNotWritable
- GuardPage
- RangeOverflow
- OriginalReadFailed
- OriginalValueMismatch
- PartialWrite
- WriteFailed
- VerificationReadFailed
- VerificationMismatch
- Cancelled
- Unknown

## 資源管理

- 使用 SafeHandle。
- 每次寫入完成後釋放必要資源。
- 不在 ViewModel 保存 Native Handle。
- Target Process 結束後立即使 Writer 失效。
- 所有 Native error code 轉換為 Result Error。

## 取消與併發

單次寫入通常很短，但 Application Service 仍接受 `CancellationToken`。

同一 Monitoring Session 的寫入預設使用互斥鎖：

```text
One write operation at a time
```

避免兩個 UI 操作同時寫入相同 Process。

## 測試目標程式

新增自行控制的 Test Target：

- 顯示 PID。
- 配置可寫數值。
- 顯示 Address。
- 可由 UI 改變數值。
- 顯示目前值。
- 支援正常關閉。
- 不包含任何第三方程式。

整合測試至少驗證：

- Int32 單次寫入。
- Float 單次寫入。
- Original value mismatch。
- Region not writable。
- Target exits before write。
- Read-back verification。
- Partial / invalid range。
- Session identity mismatch。

## 安全邊界

本階段禁止：

- 自動提升權限
- 權限繞過
- 修改記憶體保護屬性
- DLL Injection
- Code Injection
- API Hook
- Kernel Driver
- Anti-cheat / EDR 規避
- 隱藏 Handle 或行為
- Freeze / 高頻重複寫入

## 驗收標準

- 可對專案內 Test Target 執行單次寫入。
- 寫入前原值與寫入後讀回值可驗證。
- 不可寫 Region 會被拒絕。
- Target Process 結束後不再寫入。
- 所有寫入嘗試都有 Audit Log。
- Handle 與 Stream 無洩漏。
- Build、Unit Tests 與 Integration Tests 通過。

## 不在本階段處理

- Memory Editor 完整 UI。
- 多筆批次修改。
- Freeze Value。
- Script API。
- Protection override。
