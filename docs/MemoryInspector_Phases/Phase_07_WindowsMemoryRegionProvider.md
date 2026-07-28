# Phase 07 - Windows Memory Region Provider

## 相依階段

- Phase 06

## 目標

建立 Windows 虛擬記憶體區域查詢 Adapter。

## Memory Region 欄位

- Base Address
- End Address
- Size
- Allocation Base
- State
- Type
- Protection
- Readable
- Writable
- Executable
- Guard

## 原則

- Native API 只存在 Windows 專案。
- 使用 SafeHandle。
- 以 x64 位址型別處理。
- Partial failure 需回報，不得整體崩潰。

## 驗收標準

- 可列出 Session 的 Memory Regions。
- 區域屬性轉換正確。
- Guard / NoAccess 可正確辨識。
