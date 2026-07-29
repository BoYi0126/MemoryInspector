# MemoryInspector Filter Pipeline Guide

## State model

The filter pipeline keeps exactly one active round and at most one pending result.

```text
Active round
   │ Run Next/Duration filter
   ▼
Pending result
   ├─ Keep ───────→ new Active round
   └─ Discard ────→ original Active round
```

While a pending result exists, another filter cannot start. This prevents accidental history mutation and makes every transition explicit.

## Running a filter

A filter reads candidates from the active snapshot, performs bounded batch reads, applies the selected comparison, writes a new snapshot, and returns Before/After counts plus warnings. Snapshot node IDs are allocated without colliding with existing storage.

If filtering fails, is cancelled, or the Monitoring Session changes, the active round remains unchanged and incomplete storage is removed.

## Keep and discard

- **Keep** commits the pending round as active and persists its history metadata.
- **Discard** deletes the pending result and returns to its parent without rereading the process.

Review the candidate counts, duration, comparison mode, input, and warnings before keeping.

## Undo and redo

Undo moves to the parent snapshot; redo returns to an existing child. Navigation reuses snapshots and does not reread target memory. Redo is unambiguous only when the pending/child relationship is known; branching is the explicit operation for alternate history.

## Branching

Branch From Here changes the active node to a historical point, after which the next kept result becomes another child. Existing descendants remain intact. Use descriptive names immediately so sibling branches are distinguishable.

## Persistence and recovery

Round metadata records IDs, parent IDs, mode, duration, input, Before/After counts, timestamps, and storage references. History JSON is written atomically and reloaded on restart. Corrupt or inconsistent history is rejected rather than partially activated.

Legacy linear history can be migrated to the tree model. Pending state is restored only when both metadata and snapshot remain valid.

## Operational guidance

- Keep only results that represent a meaningful target state.
- Discard exploratory filters before starting unrelated work.
- Pin branches needed beyond normal temporary retention.
- Use Snapshot Compare for data-level differences between two nodes.
- Use Temporary Manager compaction after deleting large branches.

