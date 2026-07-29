# MemoryInspector Phase Development Guide

[繁體中文](README.md) | [English](README.en.md)

MemoryInspector is an x64 Windows memory-analysis platform built with **WPF, .NET 10, and MVVM**. It is read-only by default and covers process exploration, memory-region inspection, value scanning, iterative filtering, branching scan trees, disk-backed snapshots, watch entries, saved addresses, and an optional Memory Editor that is disabled by default.

## Documentation version

The current development pack follows **Master Specification v4** and contains **Phase 00–33**.

- Phase 00–23 retain their existing numbers.
- Memory Editor is split into Phase 24 Foundation, Phase 25 Windows Writer, and Phase 26 WPF UI.
- The former Phase 25–31 documents have moved to Phase 27–33.
- Phase numbers follow the actual filenames and the [phase renumbering guide](Phase_Renumbering.md).

Related documents:

- [Master Specification v4](../MemoryInspector_MasterDevelopmentSpec_v4.md)
- [Development Progress](../DevelopmentProgress.md)
- [Module Status](../ModuleStatus.md)

## Current status

| Item | Status |
|---|---|
| Completed | Phase 00–05 |
| Next phase | Phase 06 - Monitoring Session |
| Solution build | Passed, 0 warnings and 0 errors |
| Automated tests | 60 passed, 0 failed, 0 skipped |

See [DevelopmentProgress.md](../DevelopmentProgress.md) for the latest verified status.

## Core workflow

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

## Storage architecture

```text
RAM Cache
+ Binary Snapshot
+ Delta Snapshot
+ LRU Cache
+ Memory Budget
```

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
└─ MemoryInspector.IntegrationTests
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
2. [Phase 00 - Project Overview](Phase_00_ProjectOverview.md).
3. [Phase 01 - Solution Architecture](Phase_01_SolutionArchitecture.md).
4. The current Phase document.
5. Every direct dependency of the current Phase.

At completion:

1. Do not implement later Phases early.
2. Run the complete solution build and test suite.
3. Update [DevelopmentProgress.md](../DevelopmentProgress.md).
4. Update [ModuleStatus.md](../ModuleStatus.md).
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
| [00](Phase_00_ProjectOverview.md) | Project Overview | None |
| [01](Phase_01_SolutionArchitecture.md) | Solution Architecture | 00 |
| [02](Phase_02_CommonModelsAndResultPattern.md) | Common Models and Result Pattern | 01 |
| [03](Phase_03_ConfigurationLoggingAndPaths.md) | Configuration, Logging and Paths | 01, 02 |
| [04](Phase_04_ProcessExplorerCore.md) | Process Explorer Core | 02, 03 |
| [05](Phase_05_ProcessExplorerUI.md) | Process Explorer UI | 04 |
| [06](Phase_06_MonitoringSession.md) | Monitoring Session | 04, 05 |
| [07](Phase_07_WindowsMemoryRegionProvider.md) | Windows Memory Region Provider | 06 |
| [08](Phase_08_MemoryRegionViewerUI.md) | Memory Region Viewer UI | 07 |
| [09](Phase_09_MemoryReaderCore.md) | Memory Reader Core | 06, 07 |
| [10](Phase_10_ScannerFoundationAndValueParsing.md) | Scanner Foundation and Value Parsing | 02, 09 |
| [11](Phase_11_FirstScanExactValue.md) | First Scan - Exact Value | 07, 09, 10 |
| [12](Phase_12_UnknownInitialValue.md) | Unknown Initial Value | 11, 18 |
| [13](Phase_13_NextScanComparisonStrategies.md) | Next Scan Comparison Strategies | 11, 12 |
| [14](Phase_14_DurationFilter.md) | Duration Filter | 13 |
| [15](Phase_15_FilterPipeline.md) | Filter Pipeline | 13, 14 |
| [16](Phase_16_ScanHistoryAndUndo.md) | Scan History and Undo | 15 |
| [17](Phase_17_BranchingScanTree.md) | Branching Scan Tree | 16, 18 |
| [18](Phase_18_BinarySnapshotStorage.md) | Binary Snapshot Storage | 03, 10 |
| [19](Phase_19_DeltaSnapshotAndReferenceCounting.md) | Delta Snapshot and Reference Counting | 17, 18 |
| [20](Phase_20_LruCacheAndMemoryBudget.md) | LRU Cache and Memory Budget | 18, 19 |
| [21](Phase_21_ResultGridVirtualization.md) | Result Grid Virtualization | 11, 18, 20 |
| [22](Phase_22_WatchWindow.md) | Watch Window | 09, 21 |
| [23](Phase_23_SavedAddressJson.md) | Saved Address JSON | 03, 22 |
| [24](Phase_24_MemoryEditorFoundation.md) | Memory Editor Foundation | 02, 06, 09, 10, 22, 23 |
| [25](Phase_25_WindowsMemoryWriter.md) | Windows Memory Writer | 06, 07, 09, 24 |
| [26](Phase_26_MemoryEditorUI.md) | Memory Editor UI | 21, 22, 23, 24, 25 |
| [27](Phase_27_TemporaryManager.md) | Temporary Manager | 18, 19, 20 |
| [28](Phase_28_PluginFramework.md) | Plugin Framework | 01, 03 |
| [29](Phase_29_ModuleAndThreadViewer.md) | Module and Thread Viewer | 06, 07 |
| [30](Phase_30_HexViewer.md) | Hex Viewer | 09, 21 |
| [31](Phase_31_SnapshotCompare.md) | Snapshot Compare | 18, 19 |
| [32](Phase_32_IntegrationTestingAndPerformance.md) | Integration Testing and Performance | 04–31 |
| [33](Phase_33_ReleaseDocumentationAndPackaging.md) | Release Documentation and Packaging | 32 |

If a Phase document, filename, and legacy title disagree:

- For Phase 00–23, follow the Phase document's dependency section.
- For Phase 24–33, follow the new filename, this matrix, and the [phase renumbering guide](Phase_Renumbering.md).

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
