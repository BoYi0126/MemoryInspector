# MemoryInspector Module Status

| 模組 | 專案 | 狀態 | 目前範圍 |
|---|---|---|---|
| Common | `MemoryInspector.Common` | 架構完成 | 平台無關的共用基礎專案；功能自 Phase 02 開始。 |
| Core | `MemoryInspector.Core` | 架構完成 | 領域模型與規則專案；目前只有 assembly marker。 |
| Application | `MemoryInspector.Application` | 架構完成 | Use case 與 orchestration 專案；目前只有 assembly marker。 |
| Windows Adapter | `MemoryInspector.Windows` | 架構完成 | x64 Windows 平台 Adapter；尚未加入 Native API。 |
| Plugin | `MemoryInspector.Plugin` | 架構完成 | Plugin contracts 與 discovery 邊界；尚未實作 framework。 |
| WPF | `MemoryInspector.Wpf` | 架構完成 | UI composition root 與空白 MainWindow；已建立基礎 DI。 |
| Core Tests | `MemoryInspector.Core.Tests` | 基礎完成 | 已建立 MSTest runner 與平台相依邊界 smoke tests；Phase 02 功能測試尚未加入。 |
| Windows Tests | `MemoryInspector.Windows.Tests` | 基礎完成 | 已建立 MSTest runner 與 assembly smoke test；平台功能測試尚未加入。 |
| Integration Tests | `MemoryInspector.IntegrationTests` | 基礎完成 | 已建立 MSTest runner 與 composition-root smoke test。 |

## 專案相依方向

```text
MemoryInspector.Common
├─ MemoryInspector.Core
│  └─ MemoryInspector.Application
│     └─ MemoryInspector.Windows
├─ MemoryInspector.Plugin
└─ MemoryInspector.Wpf
   ├─ MemoryInspector.Application
   ├─ MemoryInspector.Windows
   └─ MemoryInspector.Plugin
```

`MemoryInspector.Wpf` 是 composition root。Core 與 Application 不依賴 WPF；Windows Native API 僅允許存在於 `MemoryInspector.Windows`。
