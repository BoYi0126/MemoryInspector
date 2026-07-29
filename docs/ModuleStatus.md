# MemoryInspector Module Status

| 模組 | 專案 | 狀態 | 目前範圍 |
|---|---|---|---|
| Common | `MemoryInspector.Common` | Phase 02 完成 | 已提供 Result Pattern、Error chaining、Guard、PagedResult、OperationProgress 與格式化工具。 |
| Core | `MemoryInspector.Core` | Phase 04 完成 | 已提供 ProcessSummary、ProcessArchitecture 與 ProcessAccessStatus；Scanner 等領域功能仍待後續 Phase。 |
| Application | `MemoryInspector.Application` | Phase 04 完成 | 已提供設定／日誌契約與 ISystemProcessService；其他 Use case 仍待後續 Phase。 |
| Windows Adapter | `MemoryInspector.Windows` | Phase 04 完成 | 已實作設定、日誌及具欄位級錯誤隔離的 Windows Process 列舉與架構偵測。 |
| Plugin | `MemoryInspector.Plugin` | 架構完成 | Plugin contracts 與 discovery 邊界；尚未實作 framework。 |
| WPF | `MemoryInspector.Wpf` | Phase 05 完成 | 已提供虛擬化 Process Explorer、非同步／自動更新、篩選、排序、詳情與 Phase 06 Monitoring 命令邊界。 |
| Core Tests | `MemoryInspector.Core.Tests` | Phase 02 完成 | 已涵蓋 Result、Error、Guard、Pagination、Progress、Byte 與 Hex 格式化及架構邊界。 |
| Windows Tests | `MemoryInspector.Windows.Tests` | Phase 04 完成 | 已涵蓋設定／日誌，以及空清單、程序結束、Access Denied、取消、CPU、handle 釋放與 live process 列舉。 |
| Integration Tests | `MemoryInspector.IntegrationTests` | Phase 05 完成 | 已涵蓋 composition root，以及 Process Explorer refresh、filter、sort、selection、auto refresh 與命令行為。 |

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
