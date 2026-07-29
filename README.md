# MemoryInspector Phase 開發指南

[繁體中文](README.md) | [English](README.en.md)

MemoryInspector 是以 **WPF、.NET 10 與 MVVM** 建立的 x64 Windows 記憶體分析平台。專案以唯讀分析為核心，支援 Process Explorer、Memory Region 檢視、數值掃描、多輪篩選、Branching Scan Tree、Disk-backed Snapshot、Watch、Saved Address，以及預設停用的可選 Memory Editor。

## 文件版本

目前 Phase 文件採用 **Master Specification v4**，共包含 **Phase 00–33**。

- Phase 00–23 維持原編號。
- Memory Editor 已拆分為 Phase 24 Foundation、Phase 25 Windows Writer、Phase 26 WPF UI。
- 原 Phase 25–31 已後移為 Phase 27–33。
- Phase 編號以實際檔名與 [Phase 編號調整說明](Phase_Renumbering.md)為準。

相關文件：

- [Master Specification v4](../MemoryInspector_MasterDevelopmentSpec_v4.md)
- [Development Progress](../DevelopmentProgress.md)
- [Module Status](../ModuleStatus.md)

## 目前進度

| 項目 | 狀態 |
|---|---|
| 已完成 | Phase 00–05 |
| 下一階段 | Phase 06 - Monitoring Session |
| Solution build | 通過，0 warnings、0 errors |
| Automated tests | 60 passed、0 failed、0 skipped |

最新結果請以 [DevelopmentProgress.md](../DevelopmentProgress.md) 為準。

## 核心流程

```text
Process Explorer
→ Monitoring Session
→ Memory Region Viewer
→ First Scan / Unknown Initial Value
→ Next Scan / Duration Filter
→ Filter Pipeline
→ Scan History / Branching Scan Tree
→ Watch / Saved Address
→ Optional Memory Editor
```

## 核心儲存架構

```text
RAM Cache
+ Binary Snapshot
+ Delta Snapshot
+ LRU Cache
+ Memory Budget
```

## Solution 結構

```text
MemoryInspector.slnx

src/
├─ MemoryInspector.Common
├─ MemoryInspector.Core
├─ MemoryInspector.Application
├─ MemoryInspector.Windows
├─ MemoryInspector.Plugin
└─ MemoryInspector.Wpf

tests/
├─ MemoryInspector.Core.Tests
├─ MemoryInspector.Windows.Tests
└─ MemoryInspector.IntegrationTests
```

相依方向保持單向：

```text
Common → Core → Application → Windows
   └────────────────────────→ Plugin

Wpf = Composition Root
Wpf → Application + Windows + Plugin
```

- Core 不依賴 WPF 或 Windows。
- Application 不依賴 View。
- Windows Adapter 封裝 Process、Native API 與平台 I/O。
- WPF 不直接宣告或呼叫 Native API。

## 建置與測試

需求：

- Windows x64
- .NET 10 SDK

在 repository root 執行：

```powershell
dotnet restore MemoryInspector.slnx
dotnet build MemoryInspector.slnx --no-restore
dotnet test MemoryInspector.slnx --no-build --no-restore
dotnet run --project src/MemoryInspector.Wpf/MemoryInspector.Wpf.csproj
```

## Phase 執行規則

每次只實作一個 Phase。開始前必須閱讀：

1. 本 README。
2. [Phase 00 - Project Overview](Phase_00_ProjectOverview.md)。
3. [Phase 01 - Solution Architecture](Phase_01_SolutionArchitecture.md)。
4. 目前 Phase 文件。
5. 目前 Phase 的所有直接相依文件。

完成時：

1. 不提前實作後續 Phase。
2. 執行完整 solution build 與 tests。
3. 更新 [DevelopmentProgress.md](../DevelopmentProgress.md)。
4. 更新 [ModuleStatus.md](../ModuleStatus.md)。
5. 列出新增、修改檔案與尚未完成項目。

建議 Prompt：

```text
請依照 Phase_XX_*.md 實作本階段。

開始前先閱讀：
- docs/MemoryInspector_Phases/README.md
- docs/MemoryInspector_Phases/Phase_00_ProjectOverview.md
- docs/MemoryInspector_Phases/Phase_01_SolutionArchitecture.md
- 目前 Phase 及其直接相依文件

限制：
1. 不要提前實作後續 Phase。
2. 完成後執行 solution build 與 tests。
3. 更新 docs/DevelopmentProgress.md 與 docs/ModuleStatus.md。
4. 列出新增、修改檔案。
5. 說明尚未完成項目。
```

## 建議開發順序

Phase 編號是需求識別碼，不代表可忽略相依直接依序實作。同一批次內的 Phase 只有在各自相依皆完成後才可並行。

| 批次 | Phase | 交付主題 |
|---:|---|---|
| 1 | 00 | 專案總覽、產品邊界與名詞 |
| 2 | 01 | Solution 與架構 |
| 3 | 02 | 共用模型與 Result Pattern |
| 4 | 03 | 設定、日誌與儲存路徑 |
| 5 | 04、28 | Process Explorer Core；Plugin Framework 基礎 |
| 6 | 05 | Process Explorer UI |
| 7 | 06 | Monitoring Session |
| 8 | 07 | Windows Memory Region Provider |
| 9 | 08、09、29 | Memory Region UI；Memory Reader；Module / Thread Viewer |
| 10 | 10 | Scanner 基礎與數值解析 |
| 11 | 11、18 | Exact Value First Scan；Binary Snapshot Storage |
| 12 | 12 | Unknown Initial Value |
| 13 | 13 | Next Scan 比對策略 |
| 14 | 14 | Duration Filter |
| 15 | 15 | Filter Pipeline |
| 16 | 16 | Scan History 與 Undo |
| 17 | 17 | Branching Scan Tree |
| 18 | 19 | Delta Snapshot 與 Reference Count |
| 19 | 20、31 | LRU Cache 與 Memory Budget；Snapshot Compare |
| 20 | 21、27 | Result Grid Virtualization；Temporary Manager |
| 21 | 22、30 | Watch Window；Hex Viewer |
| 22 | 23 | Saved Address JSON |
| 23 | 24 | Memory Editor Foundation |
| 24 | 25 | Windows Memory Writer |
| 25 | 26 | Memory Editor UI |
| 26 | 32 | 整合測試與效能驗收 |
| 27 | 33 | Release、文件與封裝 |

## Phase 相依矩陣

| Phase | 內容 | 直接相依 |
|---:|---|---|
| [00](Phase_00_ProjectOverview.md) | Project Overview | 無 |
| [01](Phase_01_SolutionArchitecture.md) | Solution Architecture | 00 |
| [02](Phase_02_CommonModelsAndResultPattern.md) | Common Models and Result Pattern | 01 |
| [03](Phase_03_ConfigurationLoggingAndPaths.md) | Configuration, Logging and Paths | 01、02 |
| [04](Phase_04_ProcessExplorerCore.md) | Process Explorer Core | 02、03 |
| [05](Phase_05_ProcessExplorerUI.md) | Process Explorer UI | 04 |
| [06](Phase_06_MonitoringSession.md) | Monitoring Session | 04、05 |
| [07](Phase_07_WindowsMemoryRegionProvider.md) | Windows Memory Region Provider | 06 |
| [08](Phase_08_MemoryRegionViewerUI.md) | Memory Region Viewer UI | 07 |
| [09](Phase_09_MemoryReaderCore.md) | Memory Reader Core | 06、07 |
| [10](Phase_10_ScannerFoundationAndValueParsing.md) | Scanner Foundation and Value Parsing | 02、09 |
| [11](Phase_11_FirstScanExactValue.md) | First Scan - Exact Value | 07、09、10 |
| [12](Phase_12_UnknownInitialValue.md) | Unknown Initial Value | 11、18 |
| [13](Phase_13_NextScanComparisonStrategies.md) | Next Scan Comparison Strategies | 11、12 |
| [14](Phase_14_DurationFilter.md) | Duration Filter | 13 |
| [15](Phase_15_FilterPipeline.md) | Filter Pipeline | 13、14 |
| [16](Phase_16_ScanHistoryAndUndo.md) | Scan History and Undo | 15 |
| [17](Phase_17_BranchingScanTree.md) | Branching Scan Tree | 16、18 |
| [18](Phase_18_BinarySnapshotStorage.md) | Binary Snapshot Storage | 03、10 |
| [19](Phase_19_DeltaSnapshotAndReferenceCounting.md) | Delta Snapshot and Reference Counting | 17、18 |
| [20](Phase_20_LruCacheAndMemoryBudget.md) | LRU Cache and Memory Budget | 18、19 |
| [21](Phase_21_ResultGridVirtualization.md) | Result Grid Virtualization | 11、18、20 |
| [22](Phase_22_WatchWindow.md) | Watch Window | 09、21 |
| [23](Phase_23_SavedAddressJson.md) | Saved Address JSON | 03、22 |
| [24](Phase_24_MemoryEditorFoundation.md) | Memory Editor Foundation | 02、06、09、10、22、23 |
| [25](Phase_25_WindowsMemoryWriter.md) | Windows Memory Writer | 06、07、09、24 |
| [26](Phase_26_MemoryEditorUI.md) | Memory Editor UI | 21、22、23、24、25 |
| [27](Phase_27_TemporaryManager.md) | Temporary Manager | 18、19、20 |
| [28](Phase_28_PluginFramework.md) | Plugin Framework | 01、03 |
| [29](Phase_29_ModuleAndThreadViewer.md) | Module and Thread Viewer | 06、07 |
| [30](Phase_30_HexViewer.md) | Hex Viewer | 09、21 |
| [31](Phase_31_SnapshotCompare.md) | Snapshot Compare | 18、19 |
| [32](Phase_32_IntegrationTestingAndPerformance.md) | Integration Testing and Performance | 04–31 |
| [33](Phase_33_ReleaseDocumentationAndPackaging.md) | Release Documentation and Packaging | 32 |

若 Phase 文件、檔名與舊標題不一致：

- Phase 00–23：以該 Phase 文件的「相依階段」為準。
- Phase 24–33：以新檔名、本文矩陣及 [Phase 編號調整說明](Phase_Renumbering.md)為準。

## 重要架構原則

- UI 不直接呼叫 Windows Native API。
- Core 不依賴 WPF。
- Windows Adapter 封裝平台實作。
- 所有長時間工作支援 `CancellationToken`。
- 大量 Candidate 不全部建立成 ViewModel。
- Scan Tree Node 不全部常駐 RAM。
- 大量 Candidate 使用 Disk-backed Storage。
- UI 使用 Virtualization 與 Pagination。
- Saved Address 與 Temporary Data 分離。
- Memory Editor 是獨立、預設停用、需明確啟用的模組。

## 安全邊界

- 預設只提供唯讀記憶體分析。
- Memory Editor 只允許用於自行開發或已授權的目標程序。
- 寫入前必須驗證 Session、Region、Address、資料型別與長度。
- 寫入後必須讀回驗證並記錄 audit。
- 不實作權限繞過、Protection override、注入、Hook、核心驅動或防護規避。
