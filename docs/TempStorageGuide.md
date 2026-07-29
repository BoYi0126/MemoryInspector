# MemoryInspector Temporary Storage Guide

## Location and ownership

MemoryInspector stores per-user data under `%LOCALAPPDATA%\MemoryInspector`.

| Directory | Contents | Temporary Manager deletion |
|---|---|---|
| `Temp` | In-progress and transient files | Yes |
| `Sessions` | Scan trees, snapshots, indexes, metadata | Yes |
| `SavedAddresses` | Named address catalogs | No |
| `Config` | Settings | No |
| `Plugins` | Installed plugins and activation state | No |
| `Logs` | Application/plugin logs | No |
| `Audit` | Memory Editor audit records | No |

## Startup cleanup

At startup, the temporary manager:

- recovers structurally valid full-snapshot temporary files;
- discards corrupt or incomplete temporary files;
- applies the configured retention period to unpinned inactive sessions;
- leaves a failure as a warning so the application can still open.

The default retention period is seven days.

## Cache and disk

RAM cache is only an acceleration layer. It is capped by byte and node-count budgets and may be evicted at any time without data loss. Disk snapshots are authoritative until explicitly deleted.

Temporary Manager reports both disk storage and current RAM cache usage.

## Safe deletion

Supported scopes are Current Node, Branch, Session, and All Temp. Before deletion:

- active scans are rejected;
- relevant cache entries are released;
- pins and snapshot references are validated;
- Saved Addresses are not touched.

All Temp means all eligible scan temporary data, not all MemoryInspector user data.

## Disk-full recovery

If a scan reports exhausted disk space:

1. Cancel the operation.
2. Open Temporary Manager and inspect the largest inactive sessions.
3. Export or pin anything important.
4. Delete eligible branches/sessions or run compaction.
5. Empty unrelated disk data if necessary.
6. Retry only after confirming sufficient free space.

Incomplete writes are not committed as valid snapshots.

## Backup and migration

Close MemoryInspector before copying `Sessions` or the whole user-data directory. Copying live snapshot files can produce an inconsistent backup.

For durable, portable data prefer Saved Address export and Snapshot Compare CSV. Raw session storage is versioned internal data and may require migration in future releases.

