# MemoryInspector Troubleshooting

## Application does not start

1. Confirm Windows and process architecture are x64.
2. Extract the entire release ZIP; do not run from inside it.
3. Confirm `MemoryInspector.Wpf.exe`, `.dll`, `.deps.json`, and `.runtimeconfig.json` files remain together.
4. Check `%LOCALAPPDATA%\MemoryInspector\Logs`.
5. Temporarily move untrusted plugin folders out of `%LOCALAPPDATA%\MemoryInspector\Plugins`.

The official package is self-contained; installing another .NET runtime should not be necessary.

## Settings are corrupt

Invalid settings are moved aside with a corrupt/invalid suffix and defaults are restored. Review the log, then reapply only known-valid values. Do not copy an old schema over the new default.

## Access denied

Access denied can occur for protected, elevated, service, or different-user processes. MemoryInspector does not elevate itself or bypass Windows protections.

- Choose a process you own and are authorized to inspect.
- Run both applications at compatible privilege levels when organizational policy permits.
- Do not disable endpoint protection to force access.

## Target exited or session became stale

Restarting a process can reuse its PID but not its original identity. Stop the stale Monitoring Session, refresh Processes, select the new instance, and start monitoring again. Revalidate Watch and Saved Addresses because addresses may have changed.

## Empty or unexpectedly large scan

- Verify value type, input format, and alignment.
- Restrict the region set to committed readable memory.
- Use aligned scanning for typed data.
- Confirm the target value actually existed during the read.
- For unknown scans, expect a large baseline and narrow it with focused Next Scans.

## Snapshot checksum, version, or history error

Do not edit snapshot/history files manually. Keep the session folder for diagnosis, switch to another valid session if available, and use Temporary Manager compaction only after preserving needed data. Corrupt snapshots are rejected rather than silently loaded.

## Disk full

Use Temporary Manager to inspect sessions, delete eligible data, and compact orphans. Saved Addresses and audits are separate and will not be removed by scan cleanup. See [Temp Storage Guide](TempStorageGuide.md).

## High memory use

Check `MemoryBudgetBytes`, cached node count, page size, and Watch entry count. Return to defaults before increasing limits:

- 512 MiB memory budget
- 3 cached nodes
- 1,000 rows per result page
- 500 ms Watch refresh

Large snapshots should remain disk-paged. If memory continues to grow, capture the release version, operation, candidate count, and logs.

## Plugin fails to load

Check:

- one plugin per subdirectory;
- valid `plugin.json`;
- entry assembly and type names;
- API/host version range;
- declared capability values;
- plugin-specific log under `Logs\Plugins\<PluginId>`.

Disable or remove the plugin folder and restart. Plugins are not sandboxed.

## Memory Editor is unavailable

The editor is disabled by default. Both safety acknowledgements are required. The target region must be committed, writable, non-Guard, and non-NoAccess; the Monitoring Session identity must still match.

A write can still fail on compare-before-write or read-back verification. Review Write History and `%LOCALAPPDATA%\MemoryInspector\Audit\MemoryEditor`.

## Preparing a diagnostic report

Include:

- MemoryInspector version and release ZIP SHA-256;
- Windows version and x64 architecture;
- exact steps and operation time;
- relevant error text;
- redacted application/plugin logs;
- whether third-party plugins were enabled.

Do not send raw snapshots, saved addresses, memory contents, process command lines, or audit records unless they have been reviewed and are safe to disclose.

