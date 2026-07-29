# MemoryInspector User Guide

## Requirements and installation

The official package supports x64 Windows 10/11. It is self-contained, so a separate .NET installation is not required.

1. Download `MemoryInspector-<version>-win-x64.zip` and its `.sha256` file.
2. Verify the archive:

   ```powershell
   Get-FileHash .\MemoryInspector-1.0.0-win-x64.zip -Algorithm SHA256
   ```

3. Extract the complete ZIP to a user-writable folder.
4. Run `MemoryInspector.Wpf.exe`.

Do not run directly from inside the ZIP. The portable package does not modify the registry or system directories.

## First launch

On first launch the application creates `%LOCALAPPDATA%\MemoryInspector` and default settings. The main window contains:

- Processes
- Memory Regions
- Results
- Watch
- Saved Addresses
- Memory Editor
- Temporary
- Plugins
- Modules & Threads
- Hex Viewer
- Snapshot Compare

The application is read-only by default. Memory Editor is disabled until both risk and authorized-target acknowledgements are accepted.

## Inspecting a process

1. Open **Processes** and refresh the list.
2. Search by name or filter by PID.
3. Select a process and review its PID, architecture, access status, memory, CPU, and start time.
4. Select **Start Monitoring**.

Monitoring binds subsequent operations to the selected process identity. If the target exits or the PID is reused, the session becomes invalid and dependent views stop or clear.

Access denied is an expected Windows security boundary. MemoryInspector does not elevate privileges or bypass process protection.

## Regions, modules, threads, and hex

- **Memory Regions** lists committed/reserved/free regions with protection, type, size, and readable/writable status. Filters and sorting operate without blocking the UI.
- **Modules & Threads** retrieves both lists independently; one partial failure does not discard valid rows from the other list.
- **Hex Viewer** opens a bounded 4 KiB window from a selected region or result. It supports address jump, byte-pattern search, refresh, and page navigation within the region. Unreadable bytes display as `??`.

## Scan engine and results

The scan engine supports exact-value first scans, unknown-initial snapshots, next-scan comparisons, duration filters, branching history, binary/delta snapshots, and bounded paging. See:

- [Scanner Guide](ScannerGuide.md)
- [Filter Pipeline Guide](FilterPipelineGuide.md)
- [Scan Tree Guide](ScanTreeGuide.md)

The stock v1.0.0 WPF shell exposes result, watch, compare, and temporary-data consumers, while scan initiation remains an Application service/host integration surface rather than a dedicated scan-command panel. Integrators can drive the scan services through the composition boundary; future shells may surface these commands directly.

## Results, Watch, and Saved Addresses

- **Results** loads only the current page, supports cancellation during rapid paging, current-page sorting, address copying, and actions to Watch or Saved Addresses.
- **Watch** batch-refreshes addresses from one Monitoring Session. Pause before changing targets. A failed address is isolated from other entries.
- **Saved Addresses** stores named addresses separately from scan temporary data. Import/export validates schema and target metadata; reconnect triggers readability validation.

Addresses are process-instance-specific. Never assume an address remains valid after restarting the target.

## Snapshot Compare

Select two nodes from the current Scan Tree, then compare them. Results classify addresses as Added, Removed, Changed, or Unchanged and provide count/storage differences. Paging and CSV export stream data without loading both snapshots into RAM.

## Temporary Manager

The **Temporary** tab reports sessions, snapshots, disk usage, and RAM cache. It can delete current-node, branch, session, or all temporary data, and compact orphaned snapshots. Pinned sessions are retained unless explicitly included.

Deletion is irreversible. Saved Addresses and Memory Editor audit files are outside scan temporary storage.

## Plugins

The release contains a sample under `samples\MemoryInspector.SamplePlugin`. To install it:

1. Close MemoryInspector.
2. Copy the whole sample folder to:

   `%LOCALAPPDATA%\MemoryInspector\Plugins\MemoryInspector.SamplePlugin`

3. Restart MemoryInspector and open **Plugins**.

Only install trusted plugins. See [Plugin Guide](PluginGuide.md).

## Memory Editor

Memory Editor is intended only for software you own, develop, test, or are explicitly authorized to inspect.

1. Read and accept both safety acknowledgements.
2. Open an address from Results, Watch, Saved Addresses, or enter an authorized manual address.
3. Review region protection, original value, parsed bytes, byte order, and compare-before-write.
4. Confirm the complete operation.
5. Review verified read-back and Write History.

The writer never elevates privileges, changes page protection, injects code, hooks APIs, or continuously freezes values. Every attempt is audited under `%LOCALAPPDATA%\MemoryInspector\Audit\MemoryEditor`.

## Test Target

`tools\MemoryInspector.TestTarget\MemoryInspector.TestTarget.exe` is a controlled process for authorized demonstrations. It prints its PID and two allocated addresses, accepts commands on standard input, and frees the allocation on exit. Do not mistake it for a test runner; no unit-test binaries are included in the release.

## Updating and uninstalling

To update, close the app and extract the new package to a new folder. Keep the old folder until the new version starts successfully. User data remains under `%LOCALAPPDATA%\MemoryInspector`.

To uninstall:

1. Close MemoryInspector.
2. Delete the extracted application folder.
3. Optionally delete `%LOCALAPPDATA%\MemoryInspector` to remove settings, snapshots, saved addresses, logs, plugins, and audit records.

