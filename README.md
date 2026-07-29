# MemoryInspector Phase 開發指南

[繁體中文](README.md) | [English](README.en.md)

MemoryInspector 是以 **WPF、.NET 10 與 MVVM** 建立的 x64 Windows 記憶體分析平台。專案以唯讀分析為核心，支援 Process Explorer、Memory Region、Module／Thread 檢視、數值掃描、多輪篩選、Branching Scan Tree、Disk-backed Snapshot、Temporary Manager、版本化 Plugin Framework、Watch、Saved Address，以及預設停用的可選 Memory Editor。

## 文件版本

目前 Phase 文件採用 **Master Specification v4**，共包含 **Phase 00–33**。

- Phase 00–23 維持原編號。
- Memory Editor 已拆分為 Phase 24 Foundation、Phase 25 Windows Writer、Phase 26 WPF UI。
- 原 Phase 25–31 已後移為 Phase 27–33。
- Phase 編號以實際檔名與 [Phase 編號調整說明](docs/MemoryInspector_Phases/Phase_Renumbering.md)為準。

相關文件：

- [Master Specification v4](docs/MemoryInspector_MasterDevelopmentSpec_v4.md)
- [Development Progress](docs/DevelopmentProgress.md)
- [Module Status](docs/ModuleStatus.md)
- [Architecture](docs/Architecture.md)
- [User Guide](docs/UserGuide.md)
- [Scanner Guide](docs/ScannerGuide.md)
- [Troubleshooting](docs/Troubleshooting.md)
- [Security and Privacy](docs/SecurityAndPrivacy.md)
- [Plugin Guide](docs/PluginGuide.md)
- [Changelog](CHANGELOG.md)
- [License](LICENSE)

## 目前進度

| 項目 | 狀態 |
|---|---|
| 已完成 | Phase 00–33 |
| 目前版本 | v1.0.0 `win-x64` self-contained |
| Solution build | 通過，0 warnings、0 errors |
| Automated tests | 402 passed、0 failed、0 skipped |
| Release smoke tests | WPF 與 Test Target 通過 |

最新結果請以 [DevelopmentProgress.md](docs/DevelopmentProgress.md) 為準。

## 核心流程

```text
Process Explorer
→ Monitoring Session
→ Memory Region Viewer
→ Hex Viewer
→ Module / Thread Viewer
→ First Scan / Unknown Initial Value
→ Next Scan / Duration Filter
→ Filter Pipeline
→ Scan History / Branching Scan Tree
→ Snapshot Compare
→ Temporary Manager
→ Optional Plugin Contributions
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

Phase 20 的 RAM cache 以磁碟 Snapshot 為唯一真實來源：100,000 筆以下的節點會預熱，較大的節點採首次讀取 lazy cache，達 1,000,000 筆則維持純磁碟分頁。預設最多快取 3 個節點並使用 512 MB candidate memory；超限時依 LRU 立即釋放 buffer，保留 Snapshot metadata 與 index。

Phase 21 的 Results 分頁一次只建立目前頁面的 row（預設最多 1,000 筆），提供 lazy loading、切頁取消、當頁排序、read status、位址複製，以及銜接 Watch／Saved Address 的動作契約；DataGrid 使用 recycling virtualization。

Phase 22 的 Watch 分頁以單一 Monitoring Session 綁定持續監看 Address，使用批次讀取更新 Previous／Current Value、Delta、Last Update 與 Status；支援 Add／Remove、型別切換、Pause／Resume、手動更新，以及 250／500／1000 ms 或 50–60,000 ms 自訂間隔。單一位址無法讀取時會獨立標示，目標程序結束時則自動停止更新。

Phase 23 的 Saved Addresses 分頁將 Key、x64 Address、ValueType、Description 與 target metadata 儲存為 schema v1 JSON。支援 Add、Rename、Update、Delete、Import、Export 與重複 Key 覆寫確認；檔案以原子方式寫入獨立的 `SavedAddresses` 目錄，不受 Scan Temp 清除影響。Monitoring Session 重新連接後會批次驗證各 Address 的可讀性，損壞或版本不支援的 JSON 會顯示錯誤且不覆寫目前 catalog。

Phase 24 建立選配 Memory Editor 的安全 Foundation。功能預設停用，啟用需接受風險及限自行開發／測試／明確授權目標的聲明；提供九種數值型別的確定 byte sequence、decimal／hex／byte-order preview、NaN／Infinity 明確策略、Session identity 與 expected-original 驗證、Mock／Denied／No-op writer，以及與一般 App Log 分離的原子 Audit JSON。

Phase 25 在 Windows Adapter 實作正式的單次記憶體寫入流程。每次操作都會重新驗證 Active Session ID 與完整程序身分，確認位址範圍位於 committed、writable、非 Guard／NoAccess 的 Region，讀取並選擇性比對原值，再以單一 SafeHandle 執行一次寫入及回讀驗證。正式 DI 已切換為 `WindowsMemoryWriter`；不提升權限、不變更頁面保護，也不提供注入、Hook 或重複 Freeze 寫入。專用 Test Target 驗證跨程序 Int32／Float 寫入、目標結束拒絕與 audit。

Phase 26 完成 Memory Editor WPF 分頁，可從 Results、Watch、Saved Addresses 或允許時的 Manual Address 開啟。Editor 會重新讀取 Region、目前值與 bytes，提供 Decimal／Hexadecimal 輸入、解析值、byte-order／byte-count preview、compare-before-write、完整確認、分類錯誤與 Verified read-back。成功後只更新目前 Result row、Watch 與 Saved Address current value，不重跑 Scan。App Session 內可對最近成功寫入執行具衝突檢查及再次確認的 Undo；Write History 支援 filter、copy、failed retry 與 CSV summary export。

Phase 27 新增 Temporary Manager WPF 分頁與 Windows 儲存服務，提供 Session／Snapshot／不完整檔案／RAM cache 統計、Current Node／Branch／Session／All Temp 刪除、保留期限自動清理、Temp folder 開啟及 Session compact。所有刪除都會先拒絕進行中的掃描並清除 LRU cache；Pinned Session 預設保留，Snapshot 透過 reference count 安全刪除。啟動時會復原可用的 Full Snapshot `.tmp`、丟棄其餘不完整檔案；Compact 只清除 orphan snapshot 並原子重寫、重新載入 Tree 驗證，不影響獨立的 Saved Addresses。

Phase 28 完成 Plugin API 1.0 與 WPF Plugin Manager，支援 Analyzer、Viewer、Exporter、Decoder、Scanner Extension manifest capability、API／Host version compatibility、Enable／Disable 原子狀態、collectible AssemblyLoadContext、每個 Plugin 獨立 DI provider、獨立 log、載入／初始化／關閉失敗隔離，以及平台中立 UI contribution。Disabled Plugin 不載入 assembly 或建立 module/service；managed DLL 從記憶體載入，停用後可立即替換。範例 Analyzer Plugin 與中英文 [Plugin Guide](docs/PluginGuide.md) 已納入。

Phase 29 新增 Session-bound Module／Thread Viewer。Windows Adapter 會先重新驗證完整 Process Identity，再列出 Module 的 Name、Base Address、Size、Path、Version，以及 Thread 的 ID、State、Priority、Start Time、CPU Time。Module 與 Thread 分開查詢；集合中途失敗回傳 partial list，個別欄位失敗則保留該列並顯示 warning。WPF 分頁提供 recycling virtualization、即時搜尋、各五種排序、降冪切換、並行 refresh 與 Session 失效自動清除。

Phase 30 新增唯讀 Hex Viewer，可從 Memory Region 或 Scan Result 直接開啟。Viewer 每次只透過既有 Session-bound Memory Reader 讀取固定 4 KiB window，並以每列 16 bytes 顯示 Address、Region-relative Offset、Hex 與 ASCII。它支援 x64 address jump、hex byte pattern search、Region-bounded page navigation 與 refresh；partial 或 failed read 仍保留完整頁面形狀，未讀取 bytes 會清楚顯示為 `??`／`·`。Session 切換或停止時會取消讀取並清除內容。

Phase 31 新增 Snapshot Compare，可從目前 Scan Tree 選擇左右節點並比較 Added、Removed、Changed、Unchanged、record count difference 與 storage size difference。Application service 對兩份 address-sorted snapshot 執行雙路 streaming merge，RAM 僅保留兩個 4,096-record storage pages 與目前 500-row view；不會同時載入完整 snapshots。WPF 分頁提供進度、summary、虛擬化差異列表與 paging。Windows exporter 使用相同比較 stream 逐列建立 CSV，成功後才原子替換目標檔，失敗時保留既有 export。

Phase 32 建立可重複執行的 Release 驗證流程，涵蓋程序於掃描中結束、Access Denied、百萬筆 Candidate、多分支與連續 Undo／Branch、Snapshot／History 損壞、磁碟空間不足錯誤映射、Memory budget、長時間 Watch、UI 快速切頁取消，以及 Memory Editor feature flag。效能測試記錄 UI orchestration latency、RAM、Snapshot 讀寫、Filter、Temp cleanup 與 live-read Handle 數；目前 402 個測試全部通過，未發現已知 Handle／Stream 洩漏。

Phase 33 完成 v1.0.0 `win-x64` self-contained portable release。發佈腳本會先執行全部 Release tests，再建立主程式、Sample Plugin 與受控 Test Target，分離 PDB 至 symbols ZIP，產生逐檔 `release-manifest.json` 與 SHA-256 sidecar，拒絕測試暫存／build intermediate 進入套件，並實際啟動封裝後的 WPF 與 Test Target。Architecture、User、Scanner、Filter Pipeline、Scan Tree、Temp Storage、Plugin、Troubleshooting、Security／Privacy、Changelog 與 License 文件均已納入。

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
├─ MemoryInspector.IntegrationTests
└─ MemoryInspector.TestTarget
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
2. [Phase 00 - Project Overview](docs/MemoryInspector_Phases/Phase_00_ProjectOverview.md)。
3. [Phase 01 - Solution Architecture](docs/MemoryInspector_Phases/Phase_01_SolutionArchitecture.md)。
4. 目前 Phase 文件。
5. 目前 Phase 的所有直接相依文件。

完成時：

1. 不提前實作後續 Phase。
2. 執行完整 solution build 與 tests。
3. 更新 [DevelopmentProgress.md](docs/DevelopmentProgress.md)。
4. 更新 [ModuleStatus.md](docs/ModuleStatus.md)。
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
| [00](docs/MemoryInspector_Phases/Phase_00_ProjectOverview.md) | Project Overview | 無 |
| [01](docs/MemoryInspector_Phases/Phase_01_SolutionArchitecture.md) | Solution Architecture | 00 |
| [02](docs/MemoryInspector_Phases/Phase_02_CommonModelsAndResultPattern.md) | Common Models and Result Pattern | 01 |
| [03](docs/MemoryInspector_Phases/Phase_03_ConfigurationLoggingAndPaths.md) | Configuration, Logging and Paths | 01、02 |
| [04](docs/MemoryInspector_Phases/Phase_04_ProcessExplorerCore.md) | Process Explorer Core | 02、03 |
| [05](docs/MemoryInspector_Phases/Phase_05_ProcessExplorerUI.md) | Process Explorer UI | 04 |
| [06](docs/MemoryInspector_Phases/Phase_06_MonitoringSession.md) | Monitoring Session | 04、05 |
| [07](docs/MemoryInspector_Phases/Phase_07_WindowsMemoryRegionProvider.md) | Windows Memory Region Provider | 06 |
| [08](docs/MemoryInspector_Phases/Phase_08_MemoryRegionViewerUI.md) | Memory Region Viewer UI | 07 |
| [09](docs/MemoryInspector_Phases/Phase_09_MemoryReaderCore.md) | Memory Reader Core | 06、07 |
| [10](docs/MemoryInspector_Phases/Phase_10_ScannerFoundationAndValueParsing.md) | Scanner Foundation and Value Parsing | 02、09 |
| [11](docs/MemoryInspector_Phases/Phase_11_FirstScanExactValue.md) | First Scan - Exact Value | 07、09、10 |
| [12](docs/MemoryInspector_Phases/Phase_12_UnknownInitialValue.md) | Unknown Initial Value | 11、18 |
| [13](docs/MemoryInspector_Phases/Phase_13_NextScanComparisonStrategies.md) | Next Scan Comparison Strategies | 11、12 |
| [14](docs/MemoryInspector_Phases/Phase_14_DurationFilter.md) | Duration Filter | 13 |
| [15](docs/MemoryInspector_Phases/Phase_15_FilterPipeline.md) | Filter Pipeline | 13、14 |
| [16](docs/MemoryInspector_Phases/Phase_16_ScanHistoryAndUndo.md) | Scan History and Undo | 15 |
| [17](docs/MemoryInspector_Phases/Phase_17_BranchingScanTree.md) | Branching Scan Tree | 16、18 |
| [18](docs/MemoryInspector_Phases/Phase_18_BinarySnapshotStorage.md) | Binary Snapshot Storage | 03、10 |
| [19](docs/MemoryInspector_Phases/Phase_19_DeltaSnapshotAndReferenceCounting.md) | Delta Snapshot and Reference Counting | 17、18 |
| [20](docs/MemoryInspector_Phases/Phase_20_LruCacheAndMemoryBudget.md) | LRU Cache and Memory Budget | 18、19 |
| [21](docs/MemoryInspector_Phases/Phase_21_ResultGridVirtualization.md) | Result Grid Virtualization | 11、18、20 |
| [22](docs/MemoryInspector_Phases/Phase_22_WatchWindow.md) | Watch Window | 09、21 |
| [23](docs/MemoryInspector_Phases/Phase_23_SavedAddressJson.md) | Saved Address JSON | 03、22 |
| [24](docs/MemoryInspector_Phases/Phase_24_MemoryEditorFoundation.md) | Memory Editor Foundation | 02、06、09、10、22、23 |
| [25](docs/MemoryInspector_Phases/Phase_25_WindowsMemoryWriter.md) | Windows Memory Writer | 06、07、09、24 |
| [26](docs/MemoryInspector_Phases/Phase_26_MemoryEditorUI.md) | Memory Editor UI | 21、22、23、24、25 |
| [27](docs/MemoryInspector_Phases/Phase_27_TemporaryManager.md) | Temporary Manager | 18、19、20 |
| [28](docs/MemoryInspector_Phases/Phase_28_PluginFramework.md) | Plugin Framework | 01、03 |
| [29](docs/MemoryInspector_Phases/Phase_29_ModuleAndThreadViewer.md) | Module and Thread Viewer | 06、07 |
| [30](docs/MemoryInspector_Phases/Phase_30_HexViewer.md) | Hex Viewer | 09、21 |
| [31](docs/MemoryInspector_Phases/Phase_31_SnapshotCompare.md) | Snapshot Compare | 18、19 |
| [32](docs/MemoryInspector_Phases/Phase_32_IntegrationTestingAndPerformance.md) | Integration Testing and Performance | 04–31 |
| [33](docs/MemoryInspector_Phases/Phase_33_ReleaseDocumentationAndPackaging.md) | Release Documentation and Packaging | 32 |

若 Phase 文件、檔名與舊標題不一致：

- Phase 00–23：以該 Phase 文件的「相依階段」為準。
- Phase 24–33：以新檔名、本文矩陣及 [Phase 編號調整說明](docs/MemoryInspector_Phases/Phase_Renumbering.md)為準。

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
