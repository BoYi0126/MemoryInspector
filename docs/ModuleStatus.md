# MemoryInspector Module Status

| 模組 | 專案 | 狀態 | 目前範圍 |
|---|---|---|---|
| Common | `MemoryInspector.Common` | Phase 02 完成 | 已提供 Result Pattern、Error chaining、Guard、PagedResult、OperationProgress 與格式化工具。 |
| Core | `MemoryInspector.Core` | Phase 29 完成 | 除 Scan／Memory Editor models 外，已提供 nullable Module／Thread 欄位與 row-level warning 純模型；不依賴 Native API。 |
| Application | `MemoryInspector.Application` | Phase 34 完成 | 已新增 Exact Initial Snapshot、Snapshot Node ID 配置及 Scan Workflow orchestration，串接 First／Next Scan、Filter Pipeline、rollback、Keep 與 Discard。 |
| Windows Adapter | `MemoryInspector.Windows` | Phase 31 完成 | 除既有 Adapter 外，已提供 Snapshot Comparison CSV streaming export、temporary-file cleanup、flush-to-disk 與 success-only atomic replace。 |
| Plugin | `MemoryInspector.Plugin` | Phase 28 完成 | 已提供 API 1.0 contracts、manifest validation、五種 capability、collectible loader、per-plugin DI、activation store、file log、failure／timeout isolation 與平台中立 UI contribution。 |
| WPF | `MemoryInspector.Wpf` | Phase 34 完成 | 已新增 Session-bound Scan Workbench，支援 Exact／Unknown First Scan、估算、Next Scan、進度／取消、Pending Keep／Discard 與 Results 導覽；Release startup smoke 通過。 |
| Core Tests | `MemoryInspector.Core.Tests` | Phase 32 驗證完成 | 126 個 Core／Common tests 通過，涵蓋 Scan matcher、Memory Editor serializer、monitoring 與跨平台純模型。 |
| Windows Tests | `MemoryInspector.Windows.Tests` | Phase 34 回歸通過 | 107 個 Adapter tests 通過；效能驗證涵蓋 Snapshot 讀寫／RAM／Stream、live-read Handle 與 Temp cleanup。 |
| Integration Tests | `MemoryInspector.IntegrationTests` | Phase 34 完成 | 175 個跨模組／UI tests 通過；新增 actual-value Exact Snapshot、Node ID 配置與 First／Next／Keep workflow 驗證，既有 Filter、Undo／Branch、Watch 與 paging 回歸皆通過。 |
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
