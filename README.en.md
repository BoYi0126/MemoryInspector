# MemoryInspector Phase Development Guide

[繁體中文](README.md) | [English](README.en.md)

MemoryInspector is an x64 Windows memory-analysis platform built with **WPF, .NET 10, and MVVM**. It is read-only by default and covers process, memory-region, module, and thread inspection; value scanning; iterative filtering; branching scan trees; disk-backed snapshots; temporary-data management; a versioned plugin framework; watch entries; saved addresses; and an optional Memory Editor that is disabled by default.

## Documentation version

The current development pack follows **Master Specification v4** and contains **Phase 00–34**.

- Phase 00–23 retain their existing numbers.
- Memory Editor is split into Phase 24 Foundation, Phase 25 Windows Writer, and Phase 26 WPF UI.
- The former Phase 25–31 documents have moved to Phase 27–33.
- Phase numbers follow the actual filenames and the [phase renumbering guide](docs/MemoryInspector_Phases/Phase_Renumbering.md).

Related documents:

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

## Current status

| Item | Status |
|---|---|
| Completed | Phase 00–34 |
| Current release | v1.0.0 `win-x64` self-contained |
| Solution build | Passed, 0 warnings and 0 errors |
| Automated tests | 408 passed, 0 failed, 0 skipped |
| Release smoke tests | WPF and Test Target passed |

See [DevelopmentProgress.md](docs/DevelopmentProgress.md) for the latest verified status.

## Core workflow

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

## Storage architecture

```text
RAM Cache
+ Binary Snapshot
+ Delta Snapshot
+ LRU Cache
+ Memory Budget
```

The Phase 20 RAM cache treats disk snapshots as the source of truth. Nodes below 100,000 records are warmed eagerly, larger nodes use a lazy first-read cache, and nodes at or above 1,000,000 records remain disk-paged. Defaults cap candidate memory at 512 MB and three cached nodes; LRU eviction releases buffers immediately while retaining snapshot metadata and indexes.

The Phase 21 Results tab creates rows only for the current page, capped at 1,000 by default. It provides lazy loading, cancellation when pages change, current-page sorting, read status, address copying, Watch/Saved Address action contracts, and recycling DataGrid virtualization.

The Phase 22 Watch tab continuously monitors addresses bound to one Monitoring Session. Batch reads update Previous/Current Value, Delta, Last Update, and Status. It supports Add/Remove, type changes, Pause/Resume, manual refresh, 250/500/1000 ms presets, and custom intervals from 50 to 60,000 ms. An unreadable address is isolated from the rest of the batch, while target-process termination stops refresh automatically.

The Phase 23 Saved Addresses tab persists keys, x64 addresses, value types, descriptions, and target metadata as schema v1 JSON. It supports Add, Rename, Update, Delete, Import, Export, and duplicate-key overwrite confirmation. Atomic writes go to the dedicated `SavedAddresses` directory and survive Scan Temp cleanup. Addresses are batch-revalidated after a Monitoring Session reconnect; corrupt or unsupported JSON produces a visible error without overwriting the current catalog.

Phase 24 establishes the optional Memory Editor's safety foundation. The feature is disabled by default and enabling it requires risk and authorized-target acknowledgements. It provides deterministic byte sequences for all nine value types, decimal/hex/byte-order previews, an explicit NaN/Infinity policy, session-identity and expected-original validation, Mock/Denied/No-op writers, and atomic audit JSON separated from normal application logs.

Phase 25 implements the production one-shot write path in the Windows Adapter. Every operation revalidates the active Session ID and full process identity, ensures the requested range is contained in a committed, writable, non-Guard/non-NoAccess region, reads and optionally compares the original value, then performs one write and read-back verification through one SafeHandle. Production DI now uses `WindowsMemoryWriter`. It does not elevate privileges, alter page protection, inject code, hook APIs, or provide repeated freeze writes. A dedicated Test Target verifies cross-process Int32/Float writes, rejection after target exit, and auditing.

Phase 26 completes the WPF Memory Editor tab with entry points from Results, Watch, Saved Addresses, and Manual Address when enabled. The editor reloads the region, current value, and bytes; supports decimal/hexadecimal input, parsed-value and byte-order/count previews, compare-before-write, complete confirmation, categorized errors, and verified read-back. A successful write refreshes only the current Result row, Watch entries, and Saved Address current values without rerunning the scan. The latest successful write in the app session can be manually undone after conflict detection and another confirmation. Write History supports filtering, copying, failed-request retry preparation, and CSV summary export.

Phase 27 adds a Temporary Manager WPF tab and Windows storage service with Session/Snapshot/incomplete-file/RAM-cache statistics, Current Node/Branch/Session/All Temp deletion, retention-based automatic cleanup, Temp-folder launch, and Session compaction. Every deletion rejects an active scan and clears the LRU cache first; pinned sessions are retained by default, and snapshots are deleted through reference-count checks. Startup recovers usable Full Snapshot `.tmp` files and discards other incomplete files. Compaction removes orphan snapshots, atomically rewrites history, and reloads the tree for verification without affecting the separate Saved Addresses store.

Phase 28 completes Plugin API 1.0 and the WPF Plugin Manager. It supports Analyzer, Viewer, Exporter, Decoder, and Scanner Extension manifest capabilities; API/host-version compatibility; atomically persisted Enable/Disable state; collectible load contexts; isolated per-plugin DI providers and logs; load/initialization/shutdown failure isolation; and platform-neutral UI contributions. A disabled plugin does not load its assembly or create its module/services. Managed DLLs are loaded from memory so package files can be replaced immediately after disable. A sample Analyzer plugin and bilingual [Plugin Guide](docs/PluginGuide.md) are included.

Phase 29 adds a session-bound Module/Thread Viewer. The Windows Adapter revalidates the complete process identity before listing module Name, Base Address, Size, Path, and Version, plus thread ID, State, Priority, Start Time, and CPU Time. Module and thread queries are independent. An enumeration failure after valid items returns a partial list, while an individual field failure keeps its row and displays a warning. The WPF tab provides recycling virtualization, immediate search, five sort choices per list, descending order, concurrent refresh, and automatic clearing when the session becomes invalid.

Phase 30 adds a read-only Hex Viewer that opens directly from a Memory Region or Scan Result. It reads only a fixed 4 KiB window through the existing session-bound Memory Reader and renders 16 bytes per row with Address, region-relative Offset, Hex, and ASCII columns. It supports x64 address jumps, hexadecimal byte-pattern search, region-bounded page navigation, and refresh. Partial or failed reads preserve the complete page shape and clearly display unread bytes as `??` and `·`. A session change or stop cancels the read and clears the viewer.

Phase 31 adds Snapshot Compare for selecting two nodes from the current Scan Tree and classifying Added, Removed, Changed, and Unchanged addresses, record-count difference, and storage-size difference. The Application service performs a two-way streaming merge over address-sorted snapshots, retaining only two 4,096-record storage pages and the current 500-row view instead of loading both snapshots into RAM. The WPF tab provides progress, summary metrics, a virtualized paged difference view, and export. The Windows exporter consumes the same comparison stream to write CSV incrementally and atomically replaces the destination only after success, preserving an existing export on failure.

Phase 32 adds a repeatable Release validation workflow covering process termination during a scan, access denial, one million candidates, multiple branches and repeated Undo/Branch operations, corrupt snapshots and history, disk-full error mapping, memory-budget enforcement, long-running Watch refresh, rapid UI paging cancellation, and the Memory Editor feature flag. Performance tests record UI orchestration latency, RAM use, snapshot read/write throughput, filtering, temporary cleanup, and live-read handle counts. All 402 tests in that phase passed with no known handle or stream leaks.

Phase 33 completes the v1.0.0 `win-x64` self-contained portable release. The publishing script runs all Release tests, publishes the application, Sample Plugin, and controlled Test Target, separates PDBs into a symbols ZIP, generates a per-file `release-manifest.json` and SHA-256 sidecars, rejects test/build artifacts from the package, and launches the packaged WPF application and Test Target as smoke tests. Architecture, user, scanner, Filter Pipeline, Scan Tree, temporary-storage, plugin, troubleshooting, security/privacy, changelog, and license documentation are included.

Phase 34 adds the Process Memory Scanner Workbench to the current source. The Scan tab provides Exact/Unknown First Scan, unknown-capture estimation, Next Scan, progress, cancellation, Pending Keep/Discard, and Results navigation. The Application workflow preserves the target process's actual matched bytes and rolls back failures across Session validation, snapshot creation, and Filter Pipeline activation. The existing v1.0.0 ZIP predates this phase; build the current source to use this UI.

## Solution structure

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

Dependencies remain unidirectional:

```text
Common → Core → Application → Windows
   └────────────────────────→ Plugin

Wpf = Composition Root
Wpf → Application + Windows + Plugin
```

- Core does not depend on WPF or Windows.
- Application does not depend on a View.
- Windows Adapter encapsulates processes, native APIs, and platform I/O.
- WPF does not declare or call native APIs directly.

## Build and test

Requirements:

- Windows x64
- .NET 10 SDK

Run from the repository root:

```powershell
dotnet restore MemoryInspector.slnx
dotnet build MemoryInspector.slnx --no-restore
dotnet test MemoryInspector.slnx --no-build --no-restore
dotnet run --project src/MemoryInspector.Wpf/MemoryInspector.Wpf.csproj
```

## Phase execution rules

Implement one Phase at a time. Before starting, read:

1. This README.
2. [Phase 00 - Project Overview](docs/MemoryInspector_Phases/Phase_00_ProjectOverview.md).
3. [Phase 01 - Solution Architecture](docs/MemoryInspector_Phases/Phase_01_SolutionArchitecture.md).
4. The current Phase document.
5. Every direct dependency of the current Phase.

At completion:

1. Do not implement later Phases early.
2. Run the complete solution build and test suite.
3. Update [DevelopmentProgress.md](docs/DevelopmentProgress.md).
4. Update [ModuleStatus.md](docs/ModuleStatus.md).
5. List added and modified files, plus anything still incomplete.

Suggested prompt:

```text
Implement the Phase described by Phase_XX_*.md.

Before starting, read:
- docs/MemoryInspector_Phases/README.md
- docs/MemoryInspector_Phases/Phase_00_ProjectOverview.md
- docs/MemoryInspector_Phases/Phase_01_SolutionArchitecture.md
- the current Phase and all of its direct dependencies

Constraints:
1. Do not implement later Phases early.
2. Run the solution build and tests when complete.
3. Update docs/DevelopmentProgress.md and docs/ModuleStatus.md.
4. List all added and modified files.
5. Describe any remaining work.
```

## Recommended development order

Phase numbers are requirement identifiers; they do not override dependencies. Phases in the same batch may run in parallel only after each Phase's own dependencies are complete.

| Batch | Phase | Deliverable |
|---:|---|---|
| 1 | 00 | Project overview, boundaries, and terminology |
| 2 | 01 | Solution architecture |
| 3 | 02 | Common models and Result Pattern |
| 4 | 03 | Configuration, logging, and paths |
| 5 | 04, 28 | Process Explorer Core; Plugin Framework foundation |
| 6 | 05 | Process Explorer UI |
| 7 | 06 | Monitoring Session |
| 8 | 07 | Windows Memory Region Provider |
| 9 | 08, 09, 29 | Memory Region UI; Memory Reader; Module / Thread Viewer |
| 10 | 10 | Scanner foundation and value parsing |
| 11 | 11, 18 | Exact Value First Scan; Binary Snapshot Storage |
| 12 | 12 | Unknown Initial Value |
| 13 | 13 | Next Scan comparison strategies |
| 14 | 14 | Duration Filter |
| 15 | 15 | Filter Pipeline |
| 16 | 16 | Scan History and Undo |
| 17 | 17 | Branching Scan Tree |
| 18 | 19 | Delta Snapshot and reference counting |
| 19 | 20, 31 | LRU Cache and Memory Budget; Snapshot Compare |
| 20 | 21, 27 | Result Grid Virtualization; Temporary Manager |
| 21 | 22, 30 | Watch Window; Hex Viewer |
| 22 | 23 | Saved Address JSON |
| 23 | 24 | Memory Editor Foundation |
| 24 | 25 | Windows Memory Writer |
| 25 | 26 | Memory Editor UI |
| 26 | 32 | Integration testing and performance |
| 27 | 33 | Release, documentation, and packaging |

## Phase dependency matrix

| Phase | Scope | Direct dependencies |
|---:|---|---|
| [00](docs/MemoryInspector_Phases/Phase_00_ProjectOverview.md) | Project Overview | None |
| [01](docs/MemoryInspector_Phases/Phase_01_SolutionArchitecture.md) | Solution Architecture | 00 |
| [02](docs/MemoryInspector_Phases/Phase_02_CommonModelsAndResultPattern.md) | Common Models and Result Pattern | 01 |
| [03](docs/MemoryInspector_Phases/Phase_03_ConfigurationLoggingAndPaths.md) | Configuration, Logging and Paths | 01, 02 |
| [04](docs/MemoryInspector_Phases/Phase_04_ProcessExplorerCore.md) | Process Explorer Core | 02, 03 |
| [05](docs/MemoryInspector_Phases/Phase_05_ProcessExplorerUI.md) | Process Explorer UI | 04 |
| [06](docs/MemoryInspector_Phases/Phase_06_MonitoringSession.md) | Monitoring Session | 04, 05 |
| [07](docs/MemoryInspector_Phases/Phase_07_WindowsMemoryRegionProvider.md) | Windows Memory Region Provider | 06 |
| [08](docs/MemoryInspector_Phases/Phase_08_MemoryRegionViewerUI.md) | Memory Region Viewer UI | 07 |
| [09](docs/MemoryInspector_Phases/Phase_09_MemoryReaderCore.md) | Memory Reader Core | 06, 07 |
| [10](docs/MemoryInspector_Phases/Phase_10_ScannerFoundationAndValueParsing.md) | Scanner Foundation and Value Parsing | 02, 09 |
| [11](docs/MemoryInspector_Phases/Phase_11_FirstScanExactValue.md) | First Scan - Exact Value | 07, 09, 10 |
| [12](docs/MemoryInspector_Phases/Phase_12_UnknownInitialValue.md) | Unknown Initial Value | 11, 18 |
| [13](docs/MemoryInspector_Phases/Phase_13_NextScanComparisonStrategies.md) | Next Scan Comparison Strategies | 11, 12 |
| [14](docs/MemoryInspector_Phases/Phase_14_DurationFilter.md) | Duration Filter | 13 |
| [15](docs/MemoryInspector_Phases/Phase_15_FilterPipeline.md) | Filter Pipeline | 13, 14 |
| [16](docs/MemoryInspector_Phases/Phase_16_ScanHistoryAndUndo.md) | Scan History and Undo | 15 |
| [17](docs/MemoryInspector_Phases/Phase_17_BranchingScanTree.md) | Branching Scan Tree | 16, 18 |
| [18](docs/MemoryInspector_Phases/Phase_18_BinarySnapshotStorage.md) | Binary Snapshot Storage | 03, 10 |
| [19](docs/MemoryInspector_Phases/Phase_19_DeltaSnapshotAndReferenceCounting.md) | Delta Snapshot and Reference Counting | 17, 18 |
| [20](docs/MemoryInspector_Phases/Phase_20_LruCacheAndMemoryBudget.md) | LRU Cache and Memory Budget | 18, 19 |
| [21](docs/MemoryInspector_Phases/Phase_21_ResultGridVirtualization.md) | Result Grid Virtualization | 11, 18, 20 |
| [22](docs/MemoryInspector_Phases/Phase_22_WatchWindow.md) | Watch Window | 09, 21 |
| [23](docs/MemoryInspector_Phases/Phase_23_SavedAddressJson.md) | Saved Address JSON | 03, 22 |
| [24](docs/MemoryInspector_Phases/Phase_24_MemoryEditorFoundation.md) | Memory Editor Foundation | 02, 06, 09, 10, 22, 23 |
| [25](docs/MemoryInspector_Phases/Phase_25_WindowsMemoryWriter.md) | Windows Memory Writer | 06, 07, 09, 24 |
| [26](docs/MemoryInspector_Phases/Phase_26_MemoryEditorUI.md) | Memory Editor UI | 21, 22, 23, 24, 25 |
| [27](docs/MemoryInspector_Phases/Phase_27_TemporaryManager.md) | Temporary Manager | 18, 19, 20 |
| [28](docs/MemoryInspector_Phases/Phase_28_PluginFramework.md) | Plugin Framework | 01, 03 |
| [29](docs/MemoryInspector_Phases/Phase_29_ModuleAndThreadViewer.md) | Module and Thread Viewer | 06, 07 |
| [30](docs/MemoryInspector_Phases/Phase_30_HexViewer.md) | Hex Viewer | 09, 21 |
| [31](docs/MemoryInspector_Phases/Phase_31_SnapshotCompare.md) | Snapshot Compare | 18, 19 |
| [32](docs/MemoryInspector_Phases/Phase_32_IntegrationTestingAndPerformance.md) | Integration Testing and Performance | 04–31 |
| [33](docs/MemoryInspector_Phases/Phase_33_ReleaseDocumentationAndPackaging.md) | Release Documentation and Packaging | 32 |

If a Phase document, filename, and legacy title disagree:

- For Phase 00–23, follow the Phase document's dependency section.
- For Phase 24–33, follow the new filename, this matrix, and the [phase renumbering guide](docs/MemoryInspector_Phases/Phase_Renumbering.md).

## Architecture principles

- UI does not call Windows native APIs directly.
- Core does not depend on WPF.
- Windows Adapter encapsulates platform implementations.
- Every long-running operation supports `CancellationToken`.
- Large candidate sets are not materialized as one ViewModel per candidate.
- Scan Tree nodes do not all remain resident in RAM.
- Large candidate sets use disk-backed storage.
- UI uses virtualization and pagination.
- Saved Addresses remain separate from Temporary Data.
- Memory Editor is isolated, disabled by default, and requires explicit enablement.

## Safety boundary

- Memory analysis is read-only by default.
- Memory Editor may only be used with self-developed or explicitly authorized target processes.
- Session, region, address, data type, and length must be validated before writing.
- Every write must be read back for verification and recorded in the audit log.
- The project does not implement privilege bypass, protection override, injection, hooks, kernel drivers, or security-control evasion.
