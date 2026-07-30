# Phase 34 - Process Memory Scanner Workbench

## 文件狀態

- 狀態：Completed / Phase 34 MVP 已實作
- 完成日期：2026-07-30
- 目的：提供可操作的單一 Process 記憶體掃描工作台
- 目標平台：x64 Windows、WPF、.NET 10、MVVM
- 主要相依階段：Phase 06～23、Phase 32、Phase 33

## 實作結果

- 新增 `ExactInitialSnapshotService`，Exact First Scan 命中時直接串流保存目標 Process 的實際 bytes，可正確支援 Float／Double tolerance 後續比較。
- 新增共用 Snapshot Node ID 配置器及 `ScanWorkflowService`，負責 First Scan、Snapshot、Filter Pipeline、失敗 rollback、Next Scan、Keep 與 Discard。
- 新增 `ScanWorkspaceViewModel` 與 WPF **Scan** 分頁，提供 Exact／Unknown First Scan、Unknown 容量估算、Next Scan、進度、取消、Pending 摘要、Keep／Discard、New Scan 與 Results 導覽。
- Scan 工作區只接受目前已連線且身分仍有效的單一 Monitoring Session；Session 失效時會取消目前操作並清除工作狀態。
- First Scan 完成後會將 Snapshot 載入 Results；Next Scan 先產生 Pending，只有 Keep 才成為新的 Active Snapshot。
- 主視窗分頁導覽已改用 `WorkspaceTab` enum，插入 Scan 分頁後 Hex Viewer、Memory Editor 等既有入口不依賴易錯的硬編碼索引。

## 驗證結果

- Release Solution build：0 warnings、0 errors。
- Core Tests：126 passed。
- Windows Tests：107 passed。
- Integration Tests：175 passed。
- 合計：408 passed、0 failed、0 skipped。
- WPF Release startup smoke test 通過。
- 新增測試確認 Float tolerance 命中時 Snapshot 保存 actual bytes、Node ID 連續配置不碰撞，以及 First → Next Pending → Keep 的協調流程。
- 下方 Test Target 全 UI click-through 保留為發行前手動驗收腳本；本次自動化未宣稱取代該手動流程。

## 使用者目標

使用者必須能完成以下完整流程：

```text
手動掃描 Process 清單
→ 選擇一個 Process
→ Start Monitoring
→ 對該 Process 的 committed + readable memory 執行 First Scan
→ 修改或操作目標程式
→ 執行一次或多次 Next Scan
→ 在 Results 查看候選位址
→ 加入 Watch 持續觀察
→ 視需要送往 Hex Viewer 或 Memory Editor
```

「掃描全部 Process」只代表列舉目前可選擇的 Process。記憶體掃描永遠只能針對目前已連線、身分已驗證的單一 Monitoring Session。

## 本階段核心結論

目前專案不是缺少整套掃描引擎，而是缺少以下兩個關鍵部分：

1. 可操作 First Scan / Next Scan 的 WPF Scanner Workbench。
2. 將 First Scan、Snapshot、Filter Pipeline、Results 串成原子工作流程的 Application 協調層。

不可只在 `MainWindow.xaml` 增加幾個按鈕後直接呼叫現有服務。現有 Exact First Scan 只回傳 Candidate Address，尚未產生可供 Next Scan 使用、包含實際初始值的 Snapshot。

## 已存在且必須重用的能力

| 能力 | 現況 | 主要位置 |
|---|---|---|
| 手動 Process 列舉與選取 | 已完成 | `ProcessExplorerViewModel`、`SystemProcessService` |
| 單一目標 Monitoring Session | 已完成 | `IMonitoringSessionService`、`MonitoringSessionService` |
| Process 身分驗證與存活檢查 | 已完成 | `MonitoringSessionIdentity`、Windows Monitoring Adapter |
| Memory Region 列舉 | 已完成 | `IMemoryRegionService`、`WindowsMemoryRegionProvider` |
| 分塊與批次讀取 | 已完成 | `IMemoryReaderService`、`MemoryReaderService` |
| 數值解析 | 已完成 | `IScanValueParser`、`InvariantScanValueParser` |
| 數值比對 | 已完成 | `IValueMatcher`、`DefaultValueMatcher` |
| Exact First Scan | 核心演算法已完成 | `IFirstScanService`、`ExactValueFirstScanService` |
| Unknown Initial Scan | 已完成並可直接建立 Snapshot | `IUnknownInitialScanService`、`UnknownInitialScanService` |
| Next Scan | 已完成 | `INextScanService`、`NextScanService` |
| Duration Filter | 已完成 | `IDurationFilterService`、`DurationFilterService` |
| Snapshot / Delta / LRU Cache | 已完成 | `ISnapshotStorage`、`BinarySnapshotStorage`、`LruSnapshotStorage` |
| Filter Pipeline / Pending / Keep / Discard | 已完成 | `IFilterPipelineService`、`FilterPipelineService` |
| Scan History / Branching Tree | Application 層已完成 | `FilterPipelineState`、`ScanTreeNode`、`JsonScanHistoryStore` |
| Result 分頁與虛擬化 | 已完成 | `ResultGridService`、`ResultGridViewModel` |
| 即時觀察數值 | 已完成 | `WatchService`、`WatchWindowViewModel` |
| Hex 檢視 | 已完成 | `HexViewerViewModel` |
| 單筆安全寫入 | 已完成 | `MemoryEditorViewModel`、Memory Write Pipeline |
| DI 註冊 | Scanner 底層服務已註冊 | `CompositionRoot.cs` |

## 已確認的功能缺口

### Gap 1：沒有 Scanner WPF 畫面

`MainWindow.xaml` 目前有 Processes、Memory Regions、Results、Watch 等分頁，但沒有可輸入掃描條件並執行 First Scan / Next Scan 的畫面。

需要新增：

- `ScanWorkspaceViewModel`
- `ScanModeOption` 或等價 UI model
- `ScanTreeNodeViewModel`（若本階段納入樹狀歷程）
- `Scan` 頂層分頁
- Progress、Cancel、Pending Result、Keep、Discard 與 View Results 操作

### Gap 2：沒有 First Scan 工作流程協調器

目前服務是獨立存在的：

```text
ExactValueFirstScanService
UnknownInitialScanService
SnapshotStorage
FilterPipelineService
ResultGridViewModel
```

沒有 Application service 負責把它們安全地串成：

```text
Validate Session
→ Reserve Snapshot Node ID
→ Run First Scan
→ Write Initial Snapshot
→ Start Filter Pipeline
→ Roll back on failure
→ Return active Snapshot
```

建議新增 `IScanWorkflowService` / `ScanWorkflowService`。名稱可調整，但此協調責任不可放在 WPF ViewModel。

### Gap 3：Exact First Scan 沒有保存實際命中值

`FirstScanResult` 目前只保存：

```text
CandidateAddress
```

Next Scan 需要：

```text
CandidateAddress + Previous Value
```

不能將使用者輸入值直接複製成所有 Candidate 的 Previous Value，原因如下：

- Float / Double Exact 使用 tolerance。
- 實際值 `12.500001` 可能符合輸入 `12.5`。
- 若 Snapshot 錯存成 `12.5`，後續 Changed / Increased / Decreased 判斷會不正確。
- Results 顯示的初始值也會失真。

必要修正：

- Exact scan 命中時必須保存當下實際讀到的 bytes。
- 應直接以 `IAsyncEnumerable<SnapshotRecord>` 串流寫入 Snapshot。
- 不可為每個命中值建立獨立大型 ViewModel 或長期保留大量 byte array。

建議新增：

```text
IExactInitialSnapshotService
ExactInitialSnapshotService
ExactInitialScanRequest
ExactInitialScanResult
```

此服務應沿用 `ExactValueFirstScanService` 的 region、chunk overlap、alignment、partial read、duplicate address、progress、cancellation 與 maximum result policy。實作時應抽出共用掃描 iterator，避免維護兩套不同的 exact matcher。

### Gap 4：Snapshot Node ID 配置不是共用能力

`FilterPipelineService` 內已有 private `ReserveNodeIdAsync`，但 First Scan 協調器無法使用。

直接假設初始 Node ID 永遠是 `1` 會在同一 Monitoring Session 執行 New Scan 時碰撞既有 Snapshot。

建議新增：

```text
ISnapshotNodeIdAllocator
SnapshotNodeIdAllocator
```

要求：

- 以 Session ID 為範圍。
- 檢查 Snapshot Storage 中既有 Node。
- 同一時間的配置必須序列化。
- `FilterPipelineService` 與 First Scan Workflow 共用同一 allocator。
- 不可覆寫已存在的 Snapshot。

### Gap 5：沒有 Scanner 的 Session 生命週期

Scanner 必須訂閱 `IMonitoringSessionService.SessionChanged`：

- Connected：允許 First Scan。
- Connecting：停用掃描按鈕。
- Disconnected / Exited / Invalidated / AccessDenied：取消執行中的掃描並清除可操作狀態。
- Session ID 改變：舊 Snapshot 不可當成新 Process 的候選資料。
- 掃描進行中 PID 被重用或 Start Time 改變：不得 commit Snapshot。

舊的暫存檔應交由 Snapshot / Temporary Manager 的既有規則處理，不可由 ViewModel 直接刪檔。

### Gap 6：Scanner 與 Results 沒有導覽事件

First Scan 或 Next Scan 完成後，必須把正確的 Snapshot 傳給：

```text
ResultGridViewModel.ShowSnapshotAsync(snapshot)
```

建議由 `ScanWorkspaceViewModel` 發出事件：

```text
SnapshotReady
ViewResultsRequested
```

再由 `MainWindowViewModel` 負責跨 ViewModel 協調與分頁導覽。不要讓 Scanner ViewModel 直接依賴 `ResultGridViewModel`。

### Gap 7：MainWindow 使用硬編碼分頁索引

`MainWindowViewModel` 目前以 `5`、`9` 導向 Memory Editor 與 Hex Viewer。新增 Scan 分頁後，所有後續索引都會位移。

本階段必須先建立：

```csharp
internal enum WorkspaceTab
{
    Processes,
    MemoryRegions,
    Scan,
    Results,
    Watch,
    SavedAddresses,
    MemoryEditor,
    Temporary,
    Plugins,
    ModulesAndThreads,
    HexViewer,
    SnapshotCompare,
}
```

所有導覽都必須使用 enum 轉換，不得再出現 magic number。

### Gap 8：Unknown Initial 缺少 Estimate 與確認 UI

`IUnknownInitialScanService.EstimateAsync` 已能提供：

- Candidate count
- Scannable bytes
- Estimated disk bytes
- Memory budget
- Snapshot threshold
- Scannable / skipped region count
- 是否需要 Disk-backed storage

UI 必須先顯示 Estimate。若容量明顯較大或 `RequiresDiskBackedStorage` 為 true，必須由使用者明確確認後才開始 Snapshot capture。

### Gap 9：Pending Result / Keep / Discard 尚未出現在 UI

`RunNextScanAsync` 會產生 Pending Result，而不是立即取代 Active Round。

UI 必須顯示：

- Before count
- After count
- Removed count
- Comparison mode
- Elapsed time
- Partial / warning count
- Keep
- Discard

有 Pending Result 時不得再次執行 Next Scan。

### Gap 10：沒有完整的真實 Process 掃描 E2E

現有 `MemoryInspector.TestTarget` 可配置 Int32 與 Float，也可透過命令修改，但目前主要用於 Memory Writer 測試。

本階段需增加真實 Windows Integration Test，至少驗證：

```text
啟動 Test Target
→ 建立 Monitoring Session
→ Exact First Scan Int32 123456789
→ Snapshot 內包含 Test Target 公布的 Int32 Address
→ SETINT 改變值
→ Next Scan Changed 或 Exact New Value
→ Pending Result 仍包含該 Address
→ Keep
→ Results 可載入該 Address
```

Unknown Initial 的大量位址案例可繼續使用 fake memory service，避免測試時間與磁碟用量失控。

## 建議 Application 架構

### `IScanWorkflowService`

建議公開下列能力；實際命名可依專案風格調整：

```csharp
public interface IScanWorkflowService
{
    Task<Result<UnknownInitialScanEstimate>> EstimateUnknownAsync(
        ScanValueType valueType,
        ScanAlignmentMode alignmentMode,
        CancellationToken cancellationToken = default);

    Task<Result<ScanWorkflowStartResult>> StartExactAsync(
        ExactInitialScanRequest request,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<Result<ScanWorkflowStartResult>> StartUnknownAsync(
        UnknownInitialWorkflowRequest request,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<Result<PendingFilterResult>> RunNextAsync(
        ScanRequest request,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<Result<FilterPipelineState>> KeepAsync(
        CancellationToken cancellationToken = default);

    Task<Result<FilterPipelineState>> DiscardAsync(
        CancellationToken cancellationToken = default);
}
```

可以直接讓 Keep / Discard 留在 `IFilterPipelineService`，但 WPF 不應自行處理「First Scan 成功、Snapshot 成功、Pipeline Start 失敗」時的 rollback。

### First Scan transaction

Exact 與 Unknown 都必須遵守：

```text
1. Capture connected Monitoring Session identity。
2. Reserve unused Snapshot Node ID。
3. 執行 scan 並串流寫入包含實際值的 Snapshot。
4. 再次確認 Session ID、PID、Start Time、Architecture 與狀態。
5. 呼叫 FilterPipelineService.StartAsync(snapshot)。
6. 若 Pipeline Start 失敗，刪除剛建立的 Snapshot。
7. 只有全部成功才發布 Active Snapshot。
```

取消或失敗時：

- 不可替換原本 Active Round。
- 不可留下已 commit 但無 History reference 的新 Snapshot。
- incomplete file 交由 Snapshot Storage 既有 recovery/cleanup 規則處理。
- Error 必須寫入既有 `IAppLogger`。

### Exact Initial Snapshot

建議資料流：

```text
Memory Regions
→ Scannable Regions
→ Chunk Read with overlap
→ Matcher
→ SnapshotRecord(Address, actual matched bytes)
→ ISnapshotStorage.WriteAsync
```

要求：

- 只掃描 `IsReadable` 且足以容納 Value Size 的 Region。
- 保留現有 aligned / unaligned 行為。
- 保留 chunk boundary overlap。
- 避免 overlapping region 的重複 address。
- 保留 maximum result limit 與 partial warning。
- progress total 使用 scannable bytes。
- 每批更新 progress，不逐 Candidate 更新 UI。

## Scanner Workbench UI

### 分頁位置

新增頂層 `Scan` 分頁，位置如下：

```text
Processes
Memory Regions
Scan
Results
Watch
Saved Addresses
...
```

頂層分頁已很多，Agent 必須確認最小視窗寬度下不會讓 Tab Header 無法操作。若出現換行或裁切，應提供可水平捲動的 Header host，或在不破壞既有導覽的前提下縮短 Header/Padding。

### 畫面區塊

#### Target 區

顯示：

- Process Name
- PID
- Start Time
- Architecture
- Monitoring Session state
- Session ID（可放 ToolTip 或詳細資訊）

未連線時：

- First Scan / Next Scan 停用。
- 顯示「Return to Processes and Start Monitoring」。

#### First Scan 區

輸入：

- First Scan Mode：Exact Value / Unknown Initial Value
- Value Type：Byte、Int16、UInt16、Int32、UInt32、Int64、UInt64、Float、Double
- Value：僅 Exact 顯示
- Alignment：Aligned / Unaligned
- Float / Double tolerance
- Maximum Results（Advanced）

按鈕：

- Estimate（Unknown）
- First Scan
- Cancel
- New Scan

#### Next Scan 區

支援：

- Exact Value
- Changed
- Unchanged
- Increased
- Decreased
- Greater Than
- Less Than

Value 欄位只在 Exact / Greater Than / Less Than 顯示並要求輸入。

第一次掃描完成後：

- Value Type 鎖定。
- Alignment 鎖定。
- Next Scan 只能使用 Active Snapshot 的 Value Type。
- 若有 Pending Result，Next Scan 停用直到 Keep 或 Discard。

#### Progress 區

使用 `OperationProgress`：

- `Total == null`：Indeterminate Loading。
- `Total != null`：顯示 ProgressBar、百分比與 completed / total。
- First Scan 的單位是 bytes。
- Next Scan 的單位是 candidate count。
- 顯示 Stage、elapsed time、warning summary。
- Cancel 必須立即可用，但不得阻塞 Dispatcher。

#### Result Summary 區

顯示：

- Active candidate count
- Pending candidate count
- Removed count
- Active round name / number
- Snapshot storage kind
- Full / Delta
- Warning / partial status

按鈕：

- View Results
- Keep
- Discard

### 建議 `ScanWorkspaceViewModel` 狀態

至少需要：

```text
CurrentSession
IsSessionConnected
SelectedFirstScanMode
SelectedValueType
SelectedComparisonMode
SelectedAlignmentMode
ValueText
ToleranceText
MaximumResults
UnknownEstimate
PipelineState
IsBusy
CanFirstScan
CanNextScan
CanCancel
CanKeep
CanDiscard
ProgressCompleted
ProgressTotal
ProgressPercentage
IsProgressIndeterminate
ProgressStage
StatusMessage
WarningMessage
ErrorMessage
```

至少需要：

```text
EstimateUnknownCommand
FirstScanCommand
NextScanCommand
CancelCommand
KeepCommand
DiscardCommand
NewScanCommand
ViewResultsCommand
```

所有 command 的 CanExecute 必須跟 Session、Busy、Pipeline 與 Pending 狀態同步。

### 必要狀態轉換

| From | 操作 / 事件 | To | 必要結果 |
|---|---|---|---|
| No Target | Monitoring Session Connected | Ready | First Scan 可用 |
| Ready | First Scan | Scanning First | 輸入鎖定、Cancel 可用 |
| Scanning First | Success | Active | Root Snapshot / Pipeline 建立 |
| Scanning First | Cancel / Failure | Ready | 不改變 Active Pipeline |
| Active | Next Scan | Scanning Next | 使用 Active Snapshot |
| Scanning Next | Success | Pending | 顯示 Before / After，Next Scan 停用 |
| Pending | Keep | Active | Pending child 成為 Active |
| Pending | Discard | Active | Parent 保持 Active |
| Active | New Scan Confirmed | Ready | 準備建立新 root，不覆寫舊 Snapshot |
| 任意狀態 | Session Disconnected / Invalidated | No Target | 取消工作並停用命令 |

同一時間只能有一個 First Scan、Next Scan、Duration Filter 或 Snapshot write operation。狀態轉換失敗時必須保持上一個已完成狀態。

## Results 與「觀察目前值」的定義

現有 Results 顯示的是 Snapshot 在該 Scan Round 捕捉到的值，不是持續即時更新值。

這是正確行為：

- Snapshot value 用於可重現的 Next Scan / Compare。
- 即時值應使用 Watch。

UI 文字不得誤導使用者。建議 Results 欄位標示為：

```text
Snapshot Value
```

使用者需要持續觀察時：

```text
Results 選取 Candidate
→ Add to Watch
→ Watch 依設定週期批次讀取目前值
```

本階段不應讓 Result Grid 自動刷新全部候選值，否則會破壞分頁效能並增加跨 Process read 負載。

## MainWindow 整合

需要修改：

- `CompositionRoot.cs`
  - 註冊 Exact Initial Snapshot service。
  - 註冊 shared Snapshot Node ID allocator。
  - 註冊 Scan Workflow service。
  - 註冊 `ScanWorkspaceViewModel`。
- `MainWindowViewModel.cs`
  - 注入 Scanner ViewModel。
  - 使用 `WorkspaceTab` enum。
  - 接收 Snapshot Ready / View Results event。
  - 呼叫 `ResultGridViewModel.ShowSnapshotAsync`。
- `MainWindow.xaml`
  - 插入 Scan 分頁。
  - 保留暗色高對比 Tab / ComboBox 樣式。
- `App.xaml.cs`
  - 只做 Scanner 的輕量初始化。
  - 不得在啟動時自動掃描 Process memory。

## New Scan 行為

New Scan 必須是使用者明確操作，且需要確認：

- 執行中的 scan 先取消。
- Pending Result 必須先 Keep 或 Discard，或在確認後明確 Discard。
- 不可自動刪除 pinned Snapshot。
- 不可覆寫既有 Snapshot Node。
- 舊的 orphan storage 由 Temporary Manager compaction 管理。

Phase 34 MVP 不要求同時維持兩個 Active Scan Workflow。單一應用程式只需要一個 Active Pipeline，且只屬於目前 Monitoring Session。

## 錯誤與安全處理

必須明確處理：

- 尚未 Start Monitoring。
- Process 在掃描中結束。
- PID 被重用。
- Session 被停止或切換。
- Access Denied。
- Region 在掃描中改變。
- Partial Read。
- Snapshot disk full。
- Maximum Results reached。
- Memory budget / disk-backed threshold。
- Snapshot checksum / serialization failure。
- 使用者取消。

禁止：

- 自動提升權限。
- 修改 page protection。
- 略過 Windows access control。
- 注入 DLL、Hook、Driver 或防護規避。
- 在 WPF 直接持有 Process Handle 或呼叫 Native API。
- 對未經使用者明確啟用的目標執行記憶體寫入。

First Scan / Next Scan 維持唯讀。寫入只能經既有 Memory Editor confirmation、compare-before-write、read-back 與 audit 流程。

## 實作順序

### Step 1：共用 Node ID 配置

1. 新增 `ISnapshotNodeIdAllocator`。
2. 將 `FilterPipelineService` 的 private 配置邏輯移到共用 service。
3. 加入同 Session collision、並行配置與 int exhaustion 測試。

### Step 2：Exact Initial Snapshot

1. 抽出 Exact Scan 共用 chunk iterator。
2. 命中時產生 `SnapshotRecord`，保存實際 bytes。
3. 串流寫入 `ISnapshotStorage`。
4. 保留現有 `IFirstScanService` 行為或以新服務相容包裝。
5. 加入 Float tolerance 實際值測試。

### Step 3：Scan Workflow

1. 新增 `IScanWorkflowService`。
2. 串接 Exact / Unknown → Snapshot → Pipeline Start。
3. 實作 rollback 與 Session revalidation。
4. 代理 Next / Keep / Discard 或提供一致 workflow API。

### Step 4：ScanWorkspaceViewModel

1. 實作輸入驗證。
2. 實作 progress 與 cancellation。
3. 實作 Session change。
4. 實作 First / Next / Pending / Keep / Discard 狀態機。
5. 發出 Snapshot / Results 導覽事件。

### Step 5：WPF Scan 分頁

1. 加入 Target、First Scan、Next Scan、Progress、Summary。
2. 確認 ComboBox / Tab 高對比樣式。
3. 確認大量資料不產生大量 UI element。
4. 確認視窗縮放、最小寬度與鍵盤操作。

### Step 6：Results 與 MainWindow 串接

1. 新增 `WorkspaceTab` enum。
2. 移除硬編碼索引。
3. Scan 完成後載入正確 Snapshot。
4. Pending Keep / Discard 後重新載入正確 Active/Pending Snapshot。

### Step 7：測試與文件

1. Application workflow tests。
2. ViewModel tests。
3. Windows Test Target E2E。
4. WPF startup smoke。
5. 更新 User Guide、Scanner Guide、README、Changelog、Development Progress 與 Module Status。

## 必要測試案例

### Application / Integration

- Exact Int32 First Scan 產生包含實際值的 Snapshot。
- Exact Float tolerance 命中時保存 target actual bytes，而非 search input bytes。
- Unknown Estimate 正確顯示 candidate 與 disk estimate。
- Unknown First Scan 成功後 Pipeline active root 正確。
- First Scan 中 Session 改變時失敗且不 commit。
- Snapshot 成功但 Pipeline Start 失敗時 rollback。
- Node ID 已存在時配置下一個可用 ID。
- Next Scan 僅讀上一輪 Candidate。
- Pending 時禁止第二次 Next Scan。
- Keep 後 Pending 成為 Active。
- Discard 後恢復 Parent Snapshot。
- Cancel 不留下可見的 incomplete Active Round。
- Maximum Results 顯示 partial / warning。

### WPF ViewModel

- 無 Monitoring Session 時 First Scan 停用。
- Connected 後 First Scan 啟用。
- Unknown 模式隱藏 Value 並要求 Estimate。
- Exact / Greater / Less 要求合法 Value。
- Float / Double tolerance 驗證。
- First Scan 後 Value Type 與 Alignment 鎖定。
- Progress 從 indeterminate 切換 determinate。
- Cancel command 取消目前操作。
- Session invalidated 取消並停用。
- Snapshot Ready event 只發布目前 Session 的結果。
- Pending 狀態正確控制 Keep / Discard / Next Scan。
- 新增 Scan tab 後 Hex Viewer / Memory Editor 導覽仍正確。

### Windows E2E

- Test Target 的 Int32 address 可由整個 Process Exact Scan 找到。
- Test Target 改值後，Next Scan Changed 可保留該 address。
- Next Scan Exact New Value 可保留該 address。
- Results 可載入該 Candidate。
- Add to Watch 後可讀取修改後的目前值。
- Test Target 關閉時 Scanner 顯示 target unavailable，且不 commit 新結果。

## 效能與 UX 驗收

- UI thread 不執行 Process memory enumeration、chunk read 或 Snapshot I/O。
- 掃描期間主視窗仍可移動、切頁與按 Cancel。
- Progress 更新必須節流或以 chunk/page 為單位，不可逐 Candidate 通知。
- Result Grid 只建立目前頁面 ViewModel。
- Unknown Initial 不把全部 Candidate 載入 RAM。
- Exact Initial Snapshot 應串流寫入，避免因保存 actual values 額外建立百萬個 byte array。
- 百萬 Candidate 仍透過既有 Snapshot paging / LRU policy。
- 掃描結束後沒有洩漏 Process Handle、FileStream 或 Test Target process。

## MVP 驗收流程

以下手動流程全部成功才算完成：

1. 啟動 `MemoryInspector.TestTarget.exe`。
2. 啟動 MemoryInspector；確認未自動掃描 Process memory。
3. 在 Processes 按 `Scan Processes`。
4. 選擇 Test Target 並按 `Start Monitoring`。
5. 到 Scan，選擇 `Int32`、`Exact Value`、輸入 `123456789`。
6. 按 First Scan；看到 bytes progress、Cancel 與完成摘要。
7. 到 Results；確認候選清單包含 Test Target 公布的 Int32 address。
8. 對 Test Target 執行 `SETINT|987654321`。
9. 回 Scan，執行 Changed 或 Exact `987654321`。
10. Pending Result 顯示 Before / After / Removed。
11. 按 Keep。
12. 到 Results，確認該 address 仍存在且 Snapshot Value 正確。
13. Add to Watch；確認 Watch 顯示目前值。
14. 關閉 Test Target；確認 Session 與 Scanner 正確失效。

## 本階段不實作

- String scan。
- Array of Bytes / wildcard pattern scan。
- Pointer scan。
- Structure dissector。
- Duration Filter 的 WPF 操作面板；既有 Application service 保留，後續可接入。
- 完整 Scan Tree 編輯器；本階段只要求 Active / Pending 摘要與 Keep / Discard。
- Freeze value。
- 批次記憶體寫入。
- 自動修改 memory protection。
- 權限提升或保護規避。
- 多個 Process 同時掃描。
- 將所有 Process memory byte 一次載入 DataGrid。
- Result Grid 對全部 Candidate 做即時輪詢。

## 文件完成後的預期驗證命令

請依序執行，避免多個測試專案同時建置共用輸出造成檔案鎖定：

```powershell
dotnet build .\MemoryInspector.slnx -c Release --no-restore
dotnet test .\tests\MemoryInspector.Core.Tests\MemoryInspector.Core.Tests.csproj -c Release --no-restore
dotnet test .\tests\MemoryInspector.Windows.Tests\MemoryInspector.Windows.Tests.csproj -c Release --no-restore
dotnet test .\tests\MemoryInspector.IntegrationTests\MemoryInspector.IntegrationTests.csproj -c Release --no-restore
```

驗收要求：

- Build：0 warnings、0 errors。
- 所有既有與新增測試通過。
- WPF 啟動 smoke test 通過。
- 沒有殘留 `MemoryInspector.TestTarget` process。
- `git diff --check` 通過。
