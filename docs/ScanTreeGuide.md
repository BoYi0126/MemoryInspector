# MemoryInspector Scan Tree Guide

## Concepts

Every kept scan round is a node. A node has one parent, zero or more children, one snapshot reference, metadata, and optional name/pin state. One node is active for a session.

```text
Baseline
├─ Standing still
│  └─ Value unchanged
└─ Moving
   ├─ Increased
   └─ Changed
```

## Navigation

- **Undo** selects the parent.
- **Redo** selects a known child.
- **Branch From Here** makes a historical node active so the next kept result creates a sibling path.
- **Path to root** explains how the selected state was derived.

Navigation uses stored snapshots and does not reread the process.

## Naming and comparison

Rename nodes using the target behavior or observation, not only the comparison operator. For example, `Menu open - health unchanged` is more useful than `Unchanged 3`.

Metadata comparison shows how two rounds were produced. Snapshot Compare performs the deeper address/value classification and can export streaming CSV.

## Pinning

Pin a node or session when it must survive retention cleanup. A pinned descendant prevents recursive deletion of the branch that owns it. Pinning protects scan storage only; it is not a backup.

## Deletion rules

- Root, active, and pending nodes cannot be deleted as ordinary branches.
- Deleting a branch recursively removes eligible descendants.
- Snapshot reference counts prevent deleting shared parents still required by delta snapshots.
- Temporary Manager clears related cache before deletion.

Deletion is irreversible. Export or back up important data first.

## Recovery and compaction

On startup, valid incomplete full snapshots may be recovered; corrupt/incomplete files are discarded. Tree metadata and snapshot indexes are validated before activation.

Compaction:

1. Blocks while a scan is active.
2. Identifies snapshots not reachable from the tree.
3. Removes only orphaned storage.
4. Rewrites history atomically.
5. Reloads the tree and snapshots for verification.

Saved Addresses, logs, plugins, and Memory Editor audits are outside Scan Tree cleanup.

