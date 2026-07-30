# Changelog

All notable changes to MemoryInspector are documented here. Versions follow Semantic Versioning.

## [Unreleased]

### Added

- Dedicated WPF Scan workbench for Exact/Unknown First Scan, estimation, Next Scan, progress, cancellation, Pending review, Keep/Discard, and Results navigation.
- Exact initial snapshot workflow that preserves the target process's actual matched bytes and atomically starts the Filter Pipeline.
- Session-bound scan orchestration with snapshot-node allocation and rollback on failed pipeline activation.

### Changed

- Main-window navigation uses named tab identifiers so the new Scan tab does not break existing Hex Viewer or Memory Editor routes.
- Scanner and User guides now document the interactive workbench while distinguishing it from the previously published v1.0.0 package.

## [1.0.0] - 2026-07-29

### Added

- x64 WPF process explorer with identity-bound Monitoring Sessions.
- Memory Region, Module, Thread, and bounded Hex viewers.
- Typed memory reading, exact/unknown scans, next scans, duration filters, Filter Pipeline, Undo/Redo, and branching Scan Tree.
- Versioned binary and delta snapshots with checksums, indexes, recovery, reference counting, LRU caching, and memory budgets.
- Virtualized Results, Watch, Saved Addresses, Snapshot Compare, and Temporary Manager workflows.
- Optional, disabled-by-default Memory Editor with validation, confirmation, one-shot verified writes, undo conflict checks, history, and dedicated audit records.
- Versioned Plugin API 1.0, isolated lifecycle, sample Analyzer plugin, and Plugin Manager.
- Release validation covering 402 functional/integration tests and seven performance tests.
- Reproducible `win-x64` self-contained portable ZIP, separate symbols ZIP, per-file release manifest, and SHA-256 sidecars.
- Architecture, user, scanner, filter, scan-tree, temporary-storage, plugin, troubleshooting, security, and privacy documentation.

### Security

- Read-only operation remains the default.
- Memory writes require explicit enablement and authorized-use acknowledgement.
- Native access is identity revalidated and SafeHandle scoped.
- Plugins are clearly documented as trusted in-process code, not sandboxed extensions.

### Known limitations

- The portable package is not code-signed and does not include MSI/MSIX registration.
- The stock WPF shell does not yet expose a dedicated first/next-scan command panel; scan orchestration is available through Application services for host integration.
- Application data is local but not encrypted by the application.
