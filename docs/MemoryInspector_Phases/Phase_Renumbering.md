# Phase 編號調整說明

為了將 Memory Editor 從單一骨架拆成可實作、可測試、可驗收的三個階段，Phase 24 之後的編號已調整。

## 新增與取代

| 新編號 | 文件 | 說明 |
|---|---|---|
| Phase 24 | `Phase_24_MemoryEditorFoundation.md` | 取代原本的 Memory Editor Skeleton，建立模型、Serializer、Feature Flag、Audit 與 Mock |
| Phase 25 | `Phase_25_WindowsMemoryWriter.md` | 新增 Windows 實際單次寫入 Adapter、Region 檢查與讀回驗證 |
| Phase 26 | `Phase_26_MemoryEditorUI.md` | 新增 WPF Editor、確認視窗、寫入歷史與有限度 Undo |

## 原 Phase 後移對照

| 原編號 | 新編號 | 功能 |
|---:|---:|---|
| 25 | 27 | Temporary Manager |
| 26 | 28 | Plugin Framework |
| 27 | 29 | Module / Thread Viewer |
| 28 | 30 | Hex Viewer |
| 29 | 31 | Snapshot Compare |
| 30 | 32 | Integration Testing and Performance |
| 31 | 33 | Release Documentation and Packaging |

## 未變更

- Phase 00 至 Phase 23 編號不變。
- 原本 `Phase_24_MemoryEditorModuleSkeleton.md` 不再使用。
- 新套件請從 Phase 24 開始改用三份新的 Memory Editor 文件。
