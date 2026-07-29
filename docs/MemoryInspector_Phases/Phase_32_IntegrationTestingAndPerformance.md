# Phase 32 - Integration Testing and Performance

## 相依階段

- Phase 04 至 Phase 31

## 目標

完成跨模組測試、壓力測試與資源驗證。

## 測試場景

- Process 在掃描中結束
- Access denied
- 百萬 Candidate
- 多分支 Scan Tree
- 連續 Undo / Branch
- Snapshot 損壞
- Temp 空間不足
- Memory budget 觸發
- Watch 長時間執行
- UI 快速切頁與取消
- Editor feature flag

## 效能指標

- UI 互動延遲
- Peak RAM
- Snapshot write speed
- Snapshot read speed
- Filter throughput
- Temp cleanup time

## 驗收標準

- 不存在已知 Handle / Stream 洩漏。
- 壓力測試 RAM 維持預算內。
- Build 與全部 Tests 通過。

## 自動化驗證

在 repository root 執行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
    -File .\scripts\Invoke-Phase32Validation.ps1
```

腳本預設使用 Release 組態，依序執行 Solution build、三個完整測試組件，以及標記為 `Performance` 的 Windows／Integration 測試。效能診斷與原始指標會寫入 `TestResults/Phase32/<timestamp>`；若只需功能驗證，可加上 `-SkipPerformanceMetrics`。

## 場景對照

| 場景 | 自動化驗證 |
|---|---|
| Process 在掃描中結束 | `UnknownInitialScanServiceTests.SessionChangeAbortsBeforeSnapshotCommit`、`NextScanServiceTests.SessionChangeAbortsWithoutCommittingNextSnapshot` |
| Access denied | `SystemProcessServiceTests.AccessDeniedFieldProducesAPartialSummary` 與 Memory Reader／Region Adapter denial 測試 |
| 百萬 Candidate | `BinarySnapshotStorageTests.StreamsOneMillionAddressRecordsAndReadsOnePage`、`ResultGridViewModelTests.MillionCandidateSnapshotKeepsOnlyCurrentPageRows` |
| 多分支 Scan Tree | `FilterPipelineServiceTests.RepeatedUndoRedoAndBranchingReuseStableHistory` |
| 連續 Undo / Branch | `FilterPipelineServiceTests.RepeatedUndoRedoAndBranchingReuseStableHistory` |
| Snapshot 損壞 | `BinarySnapshotStorageTests.ChecksumRejectsCorruptedPayload`、`FilterPipelineServiceTests.CorruptedHistoryIsRejectedWithoutChangingState` |
| Temp 空間不足 | `SnapshotStorageErrorClassifierTests.DiskFullNativeErrorsAreResourceExhausted` |
| Memory budget 觸發 | `LruSnapshotStorageTests.ByteBudgetKeepsManyBranchesWithinLimit`、`LoweringBudgetImmediatelyEvictsLeastRecentNodes` |
| Watch 長時間執行 | `WatchServiceTests.LongRunningWatchRefreshStaysMemoryBounded` |
| UI 快速切頁與取消 | `ResultGridViewModelTests.NewPageRequestCancelsPreviousLazyLoad`、`RapidPageRequestsCompleteWithLatestPageVisible` |
| Editor feature flag | `MemoryEditorFoundationTests.FeatureDefaultsDisabledAndRequiresBothAcknowledgements`、`MemoryEditorViewModelTests.DisabledFeatureKeepsWriteCommandUnavailable` |

## 效能基準

測試內建保守的跨機器驗收門檻：

| 指標 | 驗收門檻 |
|---|---:|
| 100 次快速切頁總時間 | < 5 秒，且最後一頁狀態正確 |
| Snapshot write | >= 5,000 records/s |
| Snapshot read | >= 10,000 records/s |
| Snapshot peak working-set growth | <= 256 MiB |
| Filter throughput | >= 1,000 candidates/s |
| 10,000 次 Watch refresh | >= 500 refreshes/s |
| Watch retained heap growth | <= 32 MiB |
| 1,000 個 Temp 檔案清理 | < 30 秒 |
| 500 次 live memory read | >= 25 operations/s |
| Live read Handle growth | <= 4 |

2026-07-29 在本機 x64 Release 驗證的參考結果如下；實際數值會受 CPU、磁碟與背景負載影響：

| 指標 | 實測 |
|---|---:|
| 快速切頁平均 orchestration latency | 0.17 ms/request |
| Snapshot write | 2,072,132 records/s |
| Snapshot read | 2,128,937 records/s |
| Snapshot peak working-set growth | 8,413,184 bytes |
| Filter throughput | 112,341 candidates/s |
| Watch refresh | 42,947 refreshes/s |
| Watch retained heap growth | 3,128 bytes |
| Temp cleanup | 2,735.0 ms / 1,000 files |
| Live memory read | 8,016 operations/s |
| Live read Handle growth | 0 |

## 驗證結果

- Release Solution build：0 warnings、0 errors。
- Core Tests：126 passed。
- Windows Tests：106 passed。
- Integration Tests：170 passed。
- 全部測試：402 passed、0 failed、0 skipped。
- Performance Tests：7 passed。
- Snapshot 完成後可用 `FileShare.None` 重新開啟，未發現 Stream 洩漏；重複 live read 的 Handle growth 為 0。
