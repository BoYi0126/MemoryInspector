# Phase 35 - Range Memory Workbench and Timed Monitoring

## 文件狀態

- 狀態：Planned / 尚未實作
- 建立日期：2026-07-30
- 目標平台：x64 Windows、WPF、.NET 10、MVVM
- 主要相依階段：Phase 06～15、Phase 18～22、Phase 30、Phase 34
- 目的：在同一個工作頁面完成記憶體範圍選取、分頁 Hex 檢視、範圍掃描、Next Scan 與定時 Changed／Unchanged 觀察

## 使用者目標

使用者先在 Processes 選取一個 Process 並建立 Monitoring Session，之後必須能在同一頁完成：

```text
選擇記憶體範圍
→ 確認 Start / End / Length / Region / Access
→ 分頁查看該範圍的 Hex 與 ASCII
→ 對全部 readable memory、選定範圍或目前頁面執行 First Scan
→ 執行 Next Scan
→ 或設定秒數執行 Changed / Unchanged 定時觀察
→ 在同一頁查看命中位置
→ 點選結果跳回對應 Hex 位址
→ 視需要送往 Watch、Saved Addresses、Hex Viewer 或 Memory Editor
```

本階段仍只允許操作目前已連線、身分已驗證的單一 Process。不得同時掃描多個 Process，也不得因輸入位址而繞過 Monitoring Session identity validation。

## 重要名詞

### Process Memory

Process memory 不是一段連續陣列。它是由多個 Virtual Memory Region 組成，Region 之間可能有：

- Free address space
- Reserved memory
- Uncommitted memory
- Guard page
- NoAccess page
- 不同 Protection
- 不同 Allocation Base

「查看全部記憶體」在本階段的定義是：

> 列出並可掃描目前 Process 所有 committed、readable Region，但畫面永遠只載入目前選定的一個有限大小頁面。

不得將整個 Process 的 bytes 一次載入 RAM 或建立成 DataGrid row。

### Range

Range 採半開區間：

```text
[StartAddress, EndAddressExclusive)
```

且：

```text
EndAddressExclusive = StartAddress + Length
```

UI 必須同時顯示：

- Start Address
- End Address Exclusive
- Length（bytes 及易讀格式）
- 所屬 Region Base / End
- Region Size
- State / Type / Protection
- Readable / Writable

### Hex Page

Hex Page 是 Range 內目前顯示的一小段 bytes，不等於整個 Range，也不等於整個 Process。

### Scan Scope

Scan Scope 決定 First Scan 的候選來源。Next Scan 與 Duration Filter 只處理上一輪 Snapshot 的 Candidate，不重新擴大範圍。

## 現況與需要重用的能力

| 能力 | 現況 | 主要位置 |
|---|---|---|
| Process 手動列舉與 Monitoring | 已完成 | `ProcessExplorerViewModel`、`IMonitoringSessionService` |
| Memory Region 列舉 | 已完成 | `IMemoryRegionService`、`MemoryRegionViewerViewModel` |
| Session-bound memory read | 已完成 | `IMemoryReaderService`、`MemoryReaderService` |
| 固定 4 KiB Hex Viewer | 已完成 | `HexViewerViewModel` |
| Exact / Unknown First Scan | 已完成 | `IScanWorkflowService`、`ExactInitialSnapshotService`、`UnknownInitialScanService` |
| Next Scan | 已完成 | `NextScanService`、`FilterPipelineService` |
| Duration Filter | Application 層已完成 | `IDurationFilterService`、`DurationFilterService` |
| Duration Pause / Resume | 已完成 | `DurationFilterExecutionControl` |
| Snapshot / Pending / Keep / Discard | 已完成 | `ISnapshotStorage`、`IFilterPipelineService` |
| Result paging | 已完成 | `ResultGridViewModel` |
| Watch / Saved / Memory Editor entry | 已完成 | 既有 ViewModels |

本階段必須重用以上服務，不得在 WPF ViewModel 直接呼叫 Windows Native API。

## 已確認的功能缺口

### Gap 1：沒有共用的 Range Selection model

Memory Regions、Hex Viewer 與 Scanner 各自知道部分地址資訊，但沒有一個可由 Application 層重新驗證的範圍模型。

需要建立：

- Range scope kind
- Start Address
- Length
- End Address Exclusive
- Selected Region identity
- Page size
- Normalized readable segments
- Validation result

### Gap 2：Hex Viewer Window 固定為 4 KiB

目前：

```csharp
HexViewerViewModel.WindowSizeBytes = 4 * 1024
```

使用者無法選擇 200、256、512、1024 或 4096 bytes。

### Gap 3：First Scan 只能掃描全部 eligible readable Regions

目前 Exact / Unknown Initial Scan 會自行列舉全部可讀 Region，尚未接受：

- Selected Region
- Custom Range
- Current Hex Page

### Gap 4：Duration Filter 沒有 WPF 操作面板

Application 層已有：

- Duration
- Sample Interval
- Endpoint Compare
- Continuous Observe
- Changed
- Unchanged
- Increased
- Decreased
- Pause
- Resume
- Cancel
- Progress

但使用者目前無法從 WPF 操作。

### Gap 5：Hex、Scan 與 Duration Result 不在同一頁

使用者必須在 Memory Regions、Scan、Results、Hex Viewer 之間切換，無法直接確認：

```text
目前看的 bytes
→ 使用的 Scan Scope
→ 哪些地址 Changed / Unchanged
```

### Gap 6：沒有一鍵 Timed Monitoring workflow

Duration Filter 需要前一份包含值的 Snapshot。使用者希望在尚未執行 First Scan 時，也可以：

```text
選範圍
→ 選 Changed / Unchanged
→ 設秒數
→ Start Observation
```

因此需要 Application workflow 自動完成：

```text
Estimate Unknown Baseline
→ Confirm when large
→ Create Unknown Initial Snapshot
→ Start Pipeline
→ Run Duration Filter
→ Produce Pending Result
```

## 本階段核心設計決策

### 決策 1：擴充既有 Scan 分頁，不新增另一套平行 Scanner

頂層分頁維持：

```text
Scan
```

但內容升級為：

```text
Range Memory Workbench
```

既有 Memory Regions 與 Hex Viewer 分頁保留，作為專用詳細檢視；新 Scan Workbench 提供整合操作。

### 決策 2：顯示範圍與掃描範圍分離

畫面上必須明確區分：

- Selected Range：使用者選定的完整範圍。
- Current Hex Page：目前載入畫面的有限 bytes。
- Scan Scope：First Scan 實際使用的範圍。

不可用「目前範圍」一個模糊字串同時代表三者。

### 決策 3：自訂 Range 在 MVP 必須完全位於單一 readable Region

Custom Range 必須：

- Length > 0。
- `Start + Length` 不得發生 `ulong` overflow。
- 完整落在同一個 Region。
- Region 必須為 Committed。
- Region 必須 Readable。
- 不得為 Guard。
- 不得為 NoAccess。

若跨越 Region boundary，即使下一個 Region 也可讀，MVP 仍拒絕並顯示：

```text
The selected range crosses a memory-region boundary.
Maximum valid length from this address: N bytes.
```

跨多 Region 的範圍由 `All Readable Memory` 或 `Selected Region` scope 處理，不在 Custom Range 偷偷拆段。

### 決策 4：Hex Page 大小可設定但有硬上限

支援：

- 預設：1024 bytes。
- 快速選項：200、256、512、1024、2048、4096 bytes。
- Custom：16～4096 bytes。

規則：

- Page size 必須是整數。
- 最小 16 bytes。
- 最大 4096 bytes。
- 不要求是 16 的倍數；最後一列可以少於 16 bytes。
- Page navigation 以 Selected Range Start 為基準。
- 最後一頁只讀剩餘 bytes。

例如：

```text
Selected Range = 4096 bytes
Page Size = 1024 bytes
Page Count = 4
Rows Per Full Page = 64
```

若 Page Size = 200 bytes：

```text
Page Count = 21
前 20 頁各 200 bytes
最後一頁 96 bytes
每頁最多 13 個 16-byte rows
```

### 決策 5：Scan Scope 建立 First Scan 後鎖定

支援四種 First Scan scope：

1. `AllReadableMemory`
2. `SelectedRegion`
3. `CustomRange`
4. `CurrentHexPage`

First Scan 完成後，下列設定鎖定：

- Scope
- Range
- Value Type
- Alignment

要更換必須按 New Scan，避免 Next Scan 與上一輪 Snapshot 的語意不一致。

### 決策 6：Timed Monitoring 使用「Start Observation」

Process 分頁已使用 `Start Monitoring` 表示建立 Process Monitoring Session。

為避免混淆，定時記憶體觀察的按鈕必須命名：

```text
Start Observation
```

不得只顯示 `Monitoring`。

### 決策 7：Endpoint 與 Continuous 的語意必須顯示

#### Endpoint Compare

只比較開始與結束值。

```text
10 → 20 → 10
```

結果為 Unchanged。

#### Continuous Observe

依 Sample Interval 持續取樣並累積狀態。

```text
10 → 20 → 10
```

結果為 Changed，且不是 Unchanged。

UI 必須在選項下方直接顯示說明，不可只提供 enum 名稱。

### 決策 8：同頁 Result 是 Snapshot Result，不是無限制即時輪詢

同頁結果列表顯示：

- Address
- Offset from Selected Range
- Previous / Baseline Value
- Current / Final Value
- Comparison
- Read Status

只有 Watch 分頁負責持續無限期輪詢。

## 建議 Domain / Application models

名稱可以依現有命名慣例調整，但責任不可省略。

### MemoryRange

```csharp
public sealed record MemoryRange
{
    public MemoryRange(ulong startAddress, ulong length);

    public ulong StartAddress { get; }
    public ulong EndAddressExclusive { get; }
    public ulong Length { get; }
}
```

建構時必須檢查：

- Length 不為 0。
- End 計算不 overflow。

此 model 不應依賴 WPF。

### MemoryScanScopeKind

```csharp
public enum MemoryScanScopeKind
{
    AllReadableMemory,
    SelectedRegion,
    CustomRange,
    CurrentHexPage,
}
```

### MemoryScanScopeRequest

```csharp
public sealed record MemoryScanScopeRequest
{
    public MemoryScanScopeKind Kind { get; }
    public ulong? StartAddress { get; }
    public ulong? Length { get; }
    public ulong? ExpectedRegionBase { get; }
}
```

`AllReadableMemory` 不需要 Start / Length。

其餘 Scope 必須帶足夠資料供 Application 層重新驗證，不得只相信 ViewModel 已驗證。

### MemoryScanPlan

```csharp
public sealed record MemoryScanPlan
{
    public Guid SessionId { get; }
    public MonitoringSessionIdentity TargetIdentity { get; }
    public MemoryScanScopeKind ScopeKind { get; }
    public IReadOnlyList<MemoryRangeSegment> Segments { get; }
    public ulong TotalBytes { get; }
    public int RegionCount { get; }
    public IReadOnlyList<Error> Warnings { get; }
}
```

`AllReadableMemory` 可包含多個 Segment；其他 MVP scope 只能有一個 Segment。

### MemoryRangeValidationResult

至少包含：

- IsValid
- Normalized Start
- End Exclusive
- Length
- Containing Region
- MaximumReadableLengthFromStart
- Error
- Warnings
- Display summary

### HexPageRequest / HexPageResult

建議將目前 Hex Viewer 的 page 計算與 read orchestration 下移至 Application 層：

```csharp
public sealed record HexPageRequest(
    MemoryRange Range,
    int PageSize,
    long PageNumber);
```

Result 至少包含：

- Page Number
- Page Count
- Page Start
- Page End Exclusive
- Requested Length
- Bytes Read
- Data
- Per-byte readable mask 或 unreadable ranges
- Warnings

不得讓 WPF 自行進行地址 overflow 計算。

## 建議 Application services

### IMemoryRangePlanner

```csharp
public interface IMemoryRangePlanner
{
    Task<Result<MemoryScanPlan>> CreatePlanAsync(
        MemoryScanScopeRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<MemoryRangeValidationResult>> ValidateAsync(
        MemoryRange range,
        CancellationToken cancellationToken = default);
}
```

責任：

1. 驗證 Connected Session。
2. 重新取得 Memory Regions。
3. 驗證 Process identity 未改變。
4. 套用 committed/readable policy。
5. 正規化 scope。
6. 計算 Total Bytes / Region Count。
7. 回傳可供 Scan 與 Hex 共用的 plan。

### IRangeHexPageService

```csharp
public interface IRangeHexPageService
{
    Task<Result<HexPageResult>> ReadPageAsync(
        HexPageRequest request,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
```

責任：

- 使用既有 `IMemoryReaderService`。
- 每次最多讀 4096 bytes。
- 保留 partial read。
- 將 unreadable bytes 清楚標示。
- Session 改變時拒絕發布結果。
- 支援快速換頁時取消舊 request。

### IScanWorkflowService 擴充

建議新增 overload 或 scope-aware request，不要破壞現有呼叫端：

```csharp
Task<Result<UnknownInitialScanEstimate>> EstimateUnknownAsync(
    MemoryScanScopeRequest scope,
    ScanValueType valueType,
    ScanAlignmentMode alignmentMode,
    CancellationToken cancellationToken = default);

Task<Result<ScanWorkflowStartResult>> StartExactAsync(
    MemoryScanScopeRequest scope,
    ScanRequest request,
    IProgress<OperationProgress>? progress = null,
    CancellationToken cancellationToken = default);

Task<Result<ScanWorkflowStartResult>> StartUnknownAsync(
    MemoryScanScopeRequest scope,
    ScanValueType valueType,
    ScanAlignmentMode alignmentMode,
    IProgress<OperationProgress>? progress = null,
    CancellationToken cancellationToken = default);
```

### Timed observation workflow

新增：

```csharp
Task<Result<PendingFilterResult>> RunDurationAsync(
    ScanRequest request,
    TimeSpan duration,
    DurationFilterObservationMode observationMode,
    TimeSpan sampleInterval,
    DurationFilterExecutionControl executionControl,
    IProgress<OperationProgress>? progress = null,
    CancellationToken cancellationToken = default);
```

以及一鍵 baseline workflow：

```csharp
Task<Result<PendingFilterResult>> StartUnknownAndObserveAsync(
    MemoryScanScopeRequest scope,
    ScanValueType valueType,
    ScanAlignmentMode alignmentMode,
    ScanComparisonMode comparisonMode,
    TimeSpan duration,
    DurationFilterObservationMode observationMode,
    TimeSpan sampleInterval,
    IProgress<OperationProgress>? progress = null,
    CancellationToken cancellationToken = default);
```

實際名稱可調整，但 transaction 必須由 Application service 負責。

## Range-scoped Scan 演算法要求

### All Readable Memory

沿用既有 Region policy：

- Committed
- Readable
- 非 Guard
- 非 NoAccess

對所有 eligible Region 分塊讀取。

### Selected Region / Custom Range / Current Hex Page

1. Operation 開始時重新取得 Region map。
2. 重新驗證 range 仍位於相同 readable Region。
3. 只掃描 Range 交集。
4. Chunk overlap 不得超出 Range。
5. Alignment 以全域 address 計算，不以 page offset 重新歸零。
6. Candidate Address 不得小於 Start。
7. `Address + ValueSize` 不得超過 End Exclusive。
8. Partial read warning 必須包含受影響範圍。

### Alignment

`Aligned` 必須沿用目前 scanner 的全域對齊語意。

例如 Int32：

```text
Address % 4 == 0
```

Custom Range Start 即使不是 4-byte aligned，也要從 Range 內第一個符合全域對齊的地址開始。

`Unaligned` 每次前進 1 byte。

### Unknown Initial estimate

Estimate 必須依實際 scope 計算：

- Scannable bytes
- Candidate count
- Record size
- Estimated disk bytes
- Region count
- Skipped bytes / regions
- 是否需要 disk-backed snapshot

選擇 Current Hex Page 時，不得顯示整個 Process 的 estimate。

## Timed Observation workflow

### 已有 Active Scan

```text
Validate Active Snapshot
→ Validate Session
→ Lock Scope / Type
→ Run Duration Filter
→ Produce Pending Snapshot
→ Show Before / After / Removed
→ Keep or Discard
```

### 尚無 Active Scan

按 Start Observation 時：

```text
Validate Range
→ Estimate Unknown Baseline
→ Confirm if disk-backed or above warning threshold
→ Reserve Baseline Node
→ Capture Unknown Initial values
→ Start Filter Pipeline
→ Reserve Duration Node
→ Observe for configured duration
→ Produce Pending Snapshot
```

錯誤處理：

- Baseline 失敗：不得建立 Active。
- Pipeline Start 失敗：刪除 Baseline Snapshot。
- Duration 取消或失敗：保留 Baseline Active，刪除 incomplete Pending。
- Session 失效：取消，且不得 commit Pending。
- App 關閉：正常取消，清理 incomplete temp。

### Duration 選項

Comparison：

- Changed
- Unchanged
- Increased
- Decreased

Observation Mode：

- Endpoint Compare
- Continuous Observe

輸入：

- Duration：0.1～86,400 seconds。
- Sample Interval：0.05～3,600 seconds。
- Continuous Observe 時 Sample Interval 必須小於或等於 Duration。
- Endpoint Compare 可隱藏 Sample Interval，或顯示為不適用。

預設：

```text
Comparison = Changed
Observation Mode = Continuous Observe
Duration = 5 seconds
Sample Interval = 500 ms
```

### Pause / Resume

- Pause 只暫停 observation countdown 與 sample。
- Pause 不釋放 Active Snapshot。
- UI 顯示 Paused。
- Resume 從剩餘有效 duration 繼續。
- Cancel 後回到原 Active。

## WPF 同頁設計

### 建議版面

```text
┌ Target / Session / Status / New Scan ────────────────────────────┐
├ Range & Scope ────────────────┬ Hex Page ────────────────────────┤
│ Scope                         │ Address / Offset / Hex / ASCII    │
│ Region picker                 │ Page size / Jump / Prev / Next   │
│ Start address                 │ Refresh / Search                 │
│ Length                        │ Changed-result highlight          │
│ End / Size / Access           │                                  │
│ Validate / Use Region         │                                  │
├ First / Next Scan ────────────┼ Timed Observation ───────────────┤
│ Exact / Unknown               │ Changed / Unchanged / Inc / Dec  │
│ Type / Value / Alignment      │ Endpoint / Continuous            │
│ Estimate / First / Next       │ Duration / Interval              │
│ Keep / Discard                │ Start / Pause / Resume / Cancel  │
├ Result Summary ──────────────────────────────────────────────────┤
│ Before / After / Removed / Progress / Warnings                   │
├ Paged Result Grid ───────────────────────────────────────────────┤
│ Address | Offset | Previous | Current | Comparison | Status      │
└──────────────────────────────────────────────────────────────────┘
```

若視窗寬度不足：

- Range 與 Hex 改為上下排列。
- Scan 與 Timed Observation 改為上下排列。
- 不得讓主要按鈕或錯誤訊息被裁切。

### Range & Scope 區

控制項：

- Scope ComboBox
- Region ComboBox 或 `Use selected region` action
- Start Address TextBox
- Length TextBox
- Length Unit：Bytes / KiB / MiB
- Validate Range
- Range summary
- Readability warning

Start Address：

- 接受 `0x` prefixed hexadecimal。
- 接受無 prefix 的 hexadecimal，但 UI 必須明示。
- 成功後統一顯示 `0x0000000000000000`。

Length：

- 預設使用十進位。
- 顯示換算後 bytes。
- 不得接受負數、0、NaN 或小數 bytes。

### Hex Page 區

控制項：

- Page Size ComboBox / Custom input
- First / Previous / Next / Last
- Page `N / Total`
- Jump Address
- Refresh
- Search current page
- Auto Refresh checkbox，預設關閉
- Auto Refresh interval，僅影響 Hex page，不等同 Duration Observation

DataGrid：

- 每列 16 bytes。
- Address
- Offset from Selected Range
- Hex
- ASCII
- Partial
- Match / Changed indicator

選取 Result 時：

1. 將 Hex page 導向包含該 Address 的頁面。
2. 選取對應 row。
3. 高亮對應 byte range。
4. 不自動修改記憶體。

### First / Next Scan 區

沿用 Phase 34：

- Exact Value
- Unknown Initial
- Value Type
- Value
- Alignment
- Float / Double tolerance
- Maximum Results
- Estimate
- First Scan
- Next Scan
- Pending
- Keep
- Discard

新增：

- 明確顯示目前 Scan Scope。
- 顯示 Scope bytes / Region count。
- First Scan 後鎖定 Range 與 Scope。
- `Scan Current Page` 必須實際建立 `CurrentHexPage` scope，不只是 UI filter。

### Timed Observation 區

控制項：

- Comparison
- Observation Mode
- Duration
- Sample Interval
- Start Observation
- Pause
- Resume
- Cancel
- ProgressBar
- Elapsed / Remaining
- Samples completed
- Read failures

按鈕狀態：

| 狀態 | Start | Pause | Resume | Cancel |
|---|---:|---:|---:|---:|
| Ready | Enabled | Disabled | Disabled | Disabled |
| Preparing baseline | Disabled | Disabled | Disabled | Enabled |
| Observing | Disabled | Enabled | Disabled | Enabled |
| Paused | Disabled | Disabled | Enabled | Enabled |
| Pending | Disabled | Disabled | Disabled | Disabled |
| Failed / Cancelled | Enabled | Disabled | Disabled | Disabled |

### Result 區

同頁 Result Grid 預設每頁：

```text
200 rows
```

可選：

- 100
- 200
- 500
- 1000

每列：

- Address
- Offset
- Value Type
- Previous / Baseline Value
- Current / Final Value
- Delta
- Read Status

動作：

- Jump in Hex
- Add to Watch
- Save Address
- Open full Hex Viewer
- Open Memory Editor（僅既有 feature flag 與安全確認允許時）

不得讓 Result Grid 對全部 Candidate 執行 live refresh。

## 建議 ViewModels

### RangeMemoryWorkbenchViewModel

可選擇擴充 `ScanWorkspaceViewModel`，或建立父 ViewModel 組合多個子 ViewModel。若單一類別超過合理責任，建議拆分：

```text
RangeMemoryWorkbenchViewModel
├─ MemoryRangeSelectionViewModel
├─ RangeHexViewerViewModel
├─ ScopedScanViewModel
├─ TimedObservationViewModel
└─ WorkbenchResultViewModel
```

父 ViewModel 負責：

- Session change
- 選取 Range 發布
- Scan scope lock
- Result 選取導向 Hex
- Operation mutual exclusion
- 頁面整體狀態

### 必要 properties

Range：

```text
SelectedScopeKind
AvailableRegions
SelectedRegion
StartAddressText
LengthText
SelectedLengthUnit
RangeSummary
RangeError
IsRangeValid
CanEditRange
```

Hex：

```text
SelectedPageSize
CustomPageSizeText
PageNumber
PageCount
PageRangeDisplay
HexRows
SelectedHexRow
IsHexBusy
```

Timed Observation：

```text
SelectedDurationComparison
SelectedObservationMode
DurationText
SampleIntervalText
Elapsed
Remaining
SampleCount
IsObserving
IsPaused
```

### 必要 commands

```text
RefreshRegionsCommand
UseSelectedRegionCommand
ValidateRangeCommand
ReadFirstPageCommand
PreviousHexPageCommand
NextHexPageCommand
JumpHexCommand
RefreshHexCommand
EstimateCommand
FirstScanCommand
NextScanCommand
StartObservationCommand
PauseObservationCommand
ResumeObservationCommand
CancelCommand
KeepCommand
DiscardCommand
JumpResultToHexCommand
AddResultToWatchCommand
SaveResultCommand
OpenResultInHexViewerCommand
OpenResultInMemoryEditorCommand
NewScanCommand
```

## Operation 與狀態管理

同一個 Workbench 同時只能執行一個會讀取大量 Process memory 或寫 Snapshot 的 operation：

- Hex page read 可被新 page read 取消。
- First Scan 期間不可執行 Next / Duration。
- Next Scan 期間不可執行 Duration。
- Duration 期間不可修改 Range、Type 或 Alignment。
- Pending 存在時不可開始另一個 Next / Duration。
- Pending 必須 Keep 或 Discard。
- Session invalidated 時取消全部 operation。

建議狀態：

```text
NoTarget
ReadyNoRange
Ready
ReadingHex
Estimating
ScanningFirst
Active
ScanningNext
PreparingObservation
Observing
Paused
Pending
Cancelling
TargetUnavailable
Error
```

不得只靠多個互不相干的 bool 推導所有狀態；可以使用 enum 作為主要狀態，再提供 UI 衍生 properties。

## Progress 規格

### Hex read

- 單頁讀取通常直接顯示 indeterminate。
- Partial read 後顯示 Bytes Read / Requested。

### First Scan

- Completed：scanned bytes。
- Total：scope total bytes。
- 顯示 candidate count。

### Next Scan

- Completed：processed candidates。
- Total：previous snapshot candidate count。

### Duration

至少顯示兩層資訊：

```text
Time: elapsed / duration
Candidates: processed / active count
Samples: completed count
```

Progress 更新必須節流，不得逐 byte 或逐 candidate 觸發 UI notification。

## 錯誤與警告訊息

必須區分：

### Validation

- Invalid address。
- Length must be greater than zero。
- Address + Length overflow。
- Range outside process address map。
- Range crosses region boundary。
- Region is not committed。
- Region is not readable。
- Guard / NoAccess。
- Page size outside 16～4096。
- Duration / interval invalid。

### Session

- No connected target。
- Target exited。
- PID reused / identity mismatch。
- Session changed while operation was running。

### Read

- Access denied。
- Partial read。
- Read failed at address。
- Region protection changed。

### Storage

- Insufficient disk space。
- Snapshot write failed。
- Snapshot corrupted。
- Cleanup failed。

Error message 必須包含可修正資訊，例如：

```text
Selected range length is 8,192 bytes, but only 3,072 readable bytes
remain in this region. Reduce Length to 3,072 bytes or select the
whole Region scope.
```

## Snapshot 與 History metadata

Scan History 必須可辨識初始 Scope。建議在 round metadata 增加：

- Scope Kind
- Start Address
- End Address Exclusive
- Total Bytes
- Region Count
- Page size 不需要影響 Scan identity

相容要求：

- 舊 history 沒有 scope metadata 時視為 `AllReadableMemory`。
- 若調整 history schema，必須向後相容讀取。
- 不得更改既有 binary record layout，除非有版本升級與 migration 規格。

## Test Target 擴充

目前 Test Target 只以 `Marshal.AllocHGlobal(16)` 建立兩個測試值。為了可重現 4 KiB range 驗收，建議改成：

```text
Marshal.AllocHGlobal(4096)
```

既有 READY handshake 保持四欄，避免破壞現有測試：

```text
READY|PID|INT32_ADDRESS|FLOAT_ADDRESS
```

新增命令：

```text
RANGE
→ RANGE|BASE_ADDRESS|4096

SETBYTE|OFFSET|VALUE
→ OK

FILL|VALUE
→ OK

XORRANGE|OFFSET|LENGTH|MASK
→ OK

GETBYTE|OFFSET
→ BYTE|OFFSET|VALUE
```

要求：

- Offset / Length 必須驗證，不得越界。
- VALUE / MASK 為 0～255。
- 保留既有 GET、SETINT、SETFLOAT、EXIT。
- Int32 與 Float 地址仍位於 4 KiB block 內。
- 初始 block 使用可預測 pattern，並在寫入 Int32 / Float 後啟動 command loop。
- 測試結束必須釋放 block。

## 必要測試

### Core / Application

- `MemoryRange` 正確計算 End Exclusive。
- Length 0 失敗。
- Start + Length overflow 失敗。
- Custom Range 完整位於 readable Region 時成功。
- Custom Range 跨 Region boundary 時失敗。
- Reserved / Free / Guard / NoAccess 失敗。
- MaximumReadableLengthFromStart 正確。
- All Readable Memory 產生多個 normalized segments。
- TotalBytes overflow 使用 checked / safe accumulation。
- 200-byte Hex page 最後一列為 8 bytes。
- 4096 bytes / 1024 page size 為 4 頁。
- 最後一頁只讀剩餘 bytes。
- 快速換頁取消上一個 read。
- Session changed 不發布舊 page。

### Scoped First Scan

- Exact Scan 只回傳 Selected Region 內 Candidate。
- Exact Scan 不回傳 Range 外相同值。
- Unknown Scan Candidate count 依 Custom Range 計算。
- Current Page Scan 只掃目前 page。
- Aligned Scan 使用全域 address alignment。
- Value 不得跨越 End Exclusive。
- Chunk overlap 不得產生 Range 外 Candidate。
- Estimate 使用 scope bytes，不使用 whole process bytes。
- First Scan 後 Scope / Type / Alignment 鎖定。

### Next Scan

- Next Scan 只處理 scoped baseline candidates。
- Changed / Unchanged 正確。
- Pending gate 正確。
- Keep / Discard 正確。
- New Scan 後可選新 Range。

### Duration

- Endpoint `10 → 20 → 10` 為 Unchanged。
- Continuous `10 → 20 → 10` 為 Changed。
- Continuous Unchanged 只保留全程未變地址。
- Increased / Decreased 正確累積。
- Sample Interval 驗證。
- Pause 不計入有效 observation duration。
- Resume 後完成剩餘 duration。
- Cancel 不 commit Pending。
- Read failure 排除 Candidate 並回傳 bounded warnings。
- Target exit 不 commit。
- Quick Observation 無 Active 時自動建立 Unknown baseline。
- Quick Observation baseline 成功但 Duration 失敗時保留 baseline Active。
- Large baseline 要求 confirmation。

### WPF ViewModel

- No Target 時 Range / Scan / Observation 停用。
- Connected 後載入 Regions。
- Scope 切換正確顯示欄位。
- Invalid Range 顯示原因與 maximum valid length。
- Page Size 200 / 256 / 1024 / 4096 可用。
- Result 點選後 Hex 跳到正確 page / row。
- Start Observation 的 CanExecute 正確。
- Observation 狀態控制 Pause / Resume / Cancel。
- Pending 時禁止下一個 Filter。
- Session invalidated 清除或停用內容。
- display-only binding 明確 OneWay。
- ComboBox / Tab / selected row 維持高對比。
- 低寬度視窗不裁切主要操作。

### Windows E2E

- Test Target `RANGE` 回傳 4 KiB block。
- Custom Range 可驗證為 4096 bytes。
- 1024-byte page 可翻 4 頁。
- Exact Int32 可找到 READY 公布地址。
- Current Page Exact Scan 不包含其他頁地址。
- `SETBYTE` 後 Continuous Changed 找到 `BASE + OFFSET`。
- Changed Result 點選後 Hex 跳到該 byte。
- Unchanged 結果排除被修改 byte。
- Test Target 結束後 Range / Hex / Scan / Observation 全部失效。
- 測試完成無殘留 Test Target process 或 Process Handle。

## 手動 MVP 驗收流程

### A. 範圍與 Hex 分頁

1. 啟動 `MemoryInspector.TestTarget.exe`。
2. 記下 READY 的 PID、Int32 Address 與 Float Address。
3. 在 Test Target 輸入 `RANGE`，記下 Base Address 與 Length 4096。
4. 在 MemoryInspector 的 Processes 按 Scan Processes。
5. 選擇 Test Target 並按 Start Monitoring。
6. 到 Scan Workbench。
7. Scope 選 Custom Range。
8. Start 輸入 Base Address。
9. Length 輸入 4096 bytes。
10. UI 顯示 End Exclusive、4 KiB、Region、Readable。
11. Page Size 選 1024。
12. UI 顯示 4 pages。
13. 依序 First / Next / Last page，確認 Address 與 Offset 正確。
14. Page Size 改成 200，確認 21 pages，最後一頁 96 bytes。

### B. Range Exact Scan

1. Scope 維持 Custom Range 4096 bytes。
2. First Scan 選 Exact Value。
3. Value Type 選 Int32。
4. Value 輸入 123456789。
5. Alignment 選 Aligned。
6. 按 First Scan。
7. Result 包含 READY 公布的 Int32 Address。
8. 點選 Result。
9. Hex 自動跳到該地址並高亮 4 bytes。

### C. Quick Changed Observation

1. 按 New Scan。
2. Scope 選 Custom Range 4096 bytes。
3. Value Type 選 Byte。
4. Alignment 選 Unaligned。
5. Timed Observation 選 Changed。
6. Observation Mode 選 Continuous Observe。
7. Duration 設 5 seconds。
8. Sample Interval 設 500 ms。
9. 按 Start Observation。
10. 觀察期間在 Test Target 輸入：

    ```text
    SETBYTE|100|170
    ```

11. 倒數結束後 Pending Result 包含 `BASE + 100`。
12. 點選 Result，Hex 跳至並高亮該 byte。
13. 按 Keep。

### D. Endpoint 與 Continuous 差異

1. Endpoint Compare 開始值為 10。
2. 期間改成 20，再於結束前改回 10。
3. Endpoint Changed 不保留該地址。
4. New Scan 後用相同操作執行 Continuous Observe。
5. Continuous Changed 必須保留該地址。

### E. 錯誤處理

1. 輸入會跨 Region End 的 Length。
2. UI 顯示 maximum valid length。
3. 輸入 `0xFFFFFFFFFFFFFFFF` 加 Length 2。
4. UI 顯示 address overflow，不得開始讀取。
5. Observation 期間關閉 Test Target。
6. UI 顯示 Target unavailable，且不 commit Pending。

## 效能與資源驗收

- Hex UI 同時最多建立 256 rows（4096 / 16）。
- 預設 1024-byte page 只建立 64 rows。
- Result Grid 只建立目前頁面 rows。
- All Readable Memory Scan 不將所有 bytes 載入 RAM。
- Unknown Initial 仍使用 disk-backed streaming Snapshot。
- Continuous Observe 只保存必要 flags 與 final snapshot，不保存完整時間序列。
- UI thread 不執行 Region enumeration、memory read、scan 或 snapshot I/O。
- 快速翻 Hex page 不造成舊資料覆蓋新頁。
- Progress update 不得逐 Candidate 通知。
- Cancel 後沒有 incomplete committed snapshot。
- 完成後沒有 Process Handle、FileStream 或 Test Target process 洩漏。

## 安全與產品邊界

- 預設唯讀。
- 不提升權限。
- 不變更 memory protection。
- 不繞過 Access Denied。
- 不注入 DLL。
- 不 Hook。
- 不提供 Freeze value。
- 不因顯示 Hex 而自動寫入。
- Open Memory Editor 必須沿用既有 feature flag、授權聲明、確認、compare-before-write、read-back 與 audit。
- Target exit、PID reuse 或 identity mismatch 時立即停止。

## 本階段不實作

- Inline Hex editing。
- 多筆批次寫入。
- Freeze / repeated write。
- String scan。
- Array of Bytes wildcard scan。
- Pointer scan。
- Structure dissector。
- Disassembler。
- Memory protection modification。
- 跨多 Region 的單一 Custom Range。
- 同時觀察多個 Process。
- 無上限 sample history。
- 將全部 Process bytes 顯示在單一 DataGrid。
- 對全部 Result 執行永久 live refresh。

## 建議實作順序

### Step 1：Range models 與 validation

1. 建立 `MemoryRange`。
2. 建立 Scope request / plan。
3. 建立 `IMemoryRangePlanner`。
4. 完成 overflow、Region boundary、readability tests。

### Step 2：Range Hex paging

1. 建立 `IRangeHexPageService`。
2. 將 address / page 計算移出 WPF。
3. 支援 16～4096 page size。
4. 完成 partial read、取消與 Session change tests。

### Step 3：Scoped First Scan

1. Exact Initial 接受 Scan Plan。
2. Unknown Estimate / Initial 接受 Scan Plan。
3. 保留現有 All Readable overload 相容性。
4. 完成 Range boundary / alignment tests。

### Step 4：Workflow 與 metadata

1. 擴充 `IScanWorkflowService`。
2. 保存 Scope metadata。
3. 實作 Quick Unknown Baseline + Duration transaction。
4. 完成 rollback tests。

### Step 5：Timed Observation WPF

1. Duration / interval / mode controls。
2. Start / Pause / Resume / Cancel。
3. Progress / remaining / sample count。
4. Pending Keep / Discard。

### Step 6：同頁整合

1. Range 與 Hex。
2. Scan 與 Timed Observation。
3. Result Grid。
4. Result → Hex highlight。
5. Result → Watch / Saved / full Hex / Editor。

### Step 7：Test Target 與 E2E

1. 擴充 4 KiB deterministic block。
2. 保持 READY handshake 相容。
3. 新增 RANGE / SETBYTE / FILL / XORRANGE。
4. 完成 Windows E2E 與手動驗收。

### Step 8：文件與 Release 驗證

1. 更新 User Guide。
2. 更新 Scanner Guide。
3. 更新 README / Changelog。
4. 更新 Development Progress / Module Status。
5. 執行 Release build、全部 tests 與 WPF startup smoke。

## 文件完成後的預期驗證命令

```powershell
dotnet build .\MemoryInspector.slnx -c Release --no-restore
dotnet test .\tests\MemoryInspector.Core.Tests\MemoryInspector.Core.Tests.csproj -c Release --no-restore
dotnet test .\tests\MemoryInspector.Windows.Tests\MemoryInspector.Windows.Tests.csproj -c Release --no-restore
dotnet test .\tests\MemoryInspector.IntegrationTests\MemoryInspector.IntegrationTests.csproj -c Release --no-restore
git diff --check
```

驗收要求：

- Build：0 warnings、0 errors。
- 所有既有與新增 tests 通過。
- WPF startup smoke 通過。
- 4 KiB Test Target range 手動流程通過。
- 沒有殘留 `MemoryInspector.TestTarget` process。
- 沒有新的已知 Handle / Stream leak。

