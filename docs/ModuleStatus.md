# MemoryInspector Module Status

| 模組 | 專案 | 狀態 | 目前範圍 |
|---|---|---|---|
| Common | `MemoryInspector.Common` | Phase 02 完成 | 已提供 Result Pattern、Error chaining、Guard、PagedResult、OperationProgress 與格式化工具。 |
| Core | `MemoryInspector.Core` | Phase 29 完成 | 除 Scan／Memory Editor models 外，已提供 nullable Module／Thread 欄位與 row-level warning 純模型；不依賴 Native API。 |
| Application | `MemoryInspector.Application` | Phase 31 完成 | 除既有 orchestration contracts 外，已提供 Snapshot Difference／Summary／Page models、雙 cursor streaming merge、layout validation、進度、分頁與 streaming visitor。 |
| Windows Adapter | `MemoryInspector.Windows` | Phase 31 完成 | 除既有 Adapter 外，已提供 Snapshot Comparison CSV streaming export、temporary-file cleanup、flush-to-disk 與 success-only atomic replace。 |
| Plugin | `MemoryInspector.Plugin` | Phase 28 完成 | 已提供 API 1.0 contracts、manifest validation、五種 capability、collectible loader、per-plugin DI、activation store、file log、failure／timeout isolation 與平台中立 UI contribution。 |
| WPF | `MemoryInspector.Wpf` | Phase 33 完成 | 除既有功能外，已通過封裝後實際啟動 smoke test，所有 display-only Run／TextBox／Progress binding 明確採 OneWay。 |
| Core Tests | `MemoryInspector.Core.Tests` | Phase 32 驗證完成 | 126 個 Core／Common tests 通過，涵蓋 Scan matcher、Memory Editor serializer、monitoring 與跨平台純模型。 |
| Windows Tests | `MemoryInspector.Windows.Tests` | Phase 32 完成 | 106 個 Adapter tests 通過；效能驗證涵蓋 Snapshot 讀寫／RAM／Stream、live-read Handle 與 Temp cleanup。 |
| Integration Tests | `MemoryInspector.IntegrationTests` | Phase 32 完成 | 170 個跨模組／UI tests 通過；效能驗證涵蓋 Filter、Undo／Branch、Watch 長時間執行與快速 paging cancellation。 |
| Release Packaging | `scripts/Publish-Release.ps1` | Phase 33 完成 | 產生 v1.0.0 win-x64 self-contained portable／symbols ZIP、manifest、SHA-256、Sample Plugin、Test Target，並驗證內容與啟動。 |

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
