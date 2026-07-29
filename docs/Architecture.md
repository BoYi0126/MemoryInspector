# MemoryInspector Architecture

## Overview

MemoryInspector is an x64 Windows desktop application built with WPF, .NET 10, MVVM, dependency injection, and explicit platform adapters. The architecture keeps domain models and orchestration independent from native APIs, while the Windows layer owns process handles, memory queries, reads, and optional writes.

```text
MemoryInspector.Common
└─ MemoryInspector.Core
   └─ MemoryInspector.Application
      └─ MemoryInspector.Windows

MemoryInspector.Plugin ─────────────┐
MemoryInspector.Application ────────┼─ MemoryInspector.Wpf
MemoryInspector.Windows ────────────┘
```

The executable composition root is `MemoryInspector.Wpf`. It creates one application-wide service provider and registers Windows implementations behind Application interfaces.

## Layer responsibilities

| Layer | Responsibility |
|---|---|
| Common | Result/error model, guards, paging, progress, byte/hex formatting |
| Core | Process, monitoring, memory, scan, snapshot, and write domain models; value matching and parsing |
| Application | Session-bound use cases, scan orchestration, filter pipeline, result paging, watch, saved addresses, temporary management contracts |
| Windows | Native process/memory adapters, SafeHandle ownership, binary/delta snapshot persistence, JSON stores, logging, export |
| Plugin | Versioned plugin contracts, manifest validation, isolated activation and lifecycle |
| WPF | MVVM presentation, commands, virtualization, cancellation, dialogs, clipboard, composition root |

Core and Application do not call WPF or native Windows APIs. Native resource ownership terminates in the Windows layer.

## Runtime flow

```text
Process Explorer
  → Monitoring Session identity
    → Memory Regions / Modules / Threads
    → Memory Reader
      → Scan services
        → Snapshot Storage
          → Filter Pipeline / Scan Tree
            → Results / Watch / Saved Addresses / Compare
```

A Monitoring Session captures more than a PID: it includes process start time, architecture, and session identity. Operations revalidate this identity to avoid acting on a reused PID or a process that exited and restarted.

## Memory and resource safety

- Process handles use `SafeHandle` ownership and are scoped to one operation or an explicit live connection.
- Batch reads reuse one handle within the batch.
- Snapshot writes stream records to temporary files, verify metadata/checksums, then commit by rename.
- Snapshot reads are direct-seek and paged; million-record datasets are not loaded into one UI collection.
- LRU caching enforces both byte and node-count budgets. Disk snapshots remain the source of truth.
- Cancellation tokens flow through long-running reads, scans, filtering, paging, exports, and cleanup.
- Errors cross layers as structured `Result` values; expected access, I/O, validation, cancellation, and stale-session failures are not represented as UI exceptions.

## Persistence

Application data is stored under:

```text
%LOCALAPPDATA%\MemoryInspector\
├─ Config\settings.json
├─ Temp\
├─ Sessions\
├─ SavedAddresses\
├─ Plugins\
├─ Logs\
└─ Audit\MemoryEditor\
```

Settings, saved addresses, plugin activation state, scan history, and audit records use versioned formats. Critical JSON writes and exports use temporary files followed by success-only replacement. Binary snapshots contain versioned headers, fixed record layouts, indexes, and checksums.

## Plugin boundary

Each enabled plugin receives its own collectible `AssemblyLoadContext` and service provider. The host exposes only plugin contracts, a plugin context, logger, and `TimeProvider`; it does not expose the application root provider or memory/session services.

This is dependency and lifecycle isolation, not a security sandbox. Plugin code executes in the host process and must be trusted.

## Release layout

The official package is `win-x64` self-contained and folder-based. Trimming and single-file publishing are disabled because WPF, dependency injection, and plugin discovery use metadata and reflection. PDB files are distributed separately in a version-matched symbols ZIP.

