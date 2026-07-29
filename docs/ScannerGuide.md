# MemoryInspector Scanner Guide

## Scope

The scanner operates only against an active, identity-validated Monitoring Session. It reads committed, readable regions through the Memory Reader and writes candidate data to bounded memory or versioned snapshots. It does not change page protection or write target memory.

In v1.0.0 the stock WPF shell does not provide a dedicated scan-command panel; these workflows are Application service surfaces for host or plugin integration. Result paging, Watch, Snapshot Compare, and temporary management are available in the shell.

## Value types

Supported types are Byte, Int16, UInt16, Int32, UInt32, Int64, UInt64, Float, and Double. Integer input supports invariant decimal and explicit hexadecimal forms. Floating-point parsing is invariant and uses a configurable tolerance for comparisons.

All values are encoded deterministically. Scans use the target bytes and selected value type; selecting the wrong type changes both record width and comparison semantics.

## Alignment

- **Aligned** advances by the selected value size.
- **Unaligned** advances by one byte.

Aligned scans are faster and usually appropriate for typed values. Unaligned scans find packed or offset values but create more candidates and I/O.

## Exact-value first scan

An exact scan:

1. Validates the value and comparison request.
2. Enumerates eligible readable regions.
3. Reads bounded chunks with overlap so a value crossing a chunk boundary is not missed.
4. Matches candidates without duplicating addresses from overlapping regions.
5. Reports chunk-level progress and supports cancellation.

Use a conservative result limit when scanning a large address space.

## Unknown initial value

Unknown-initial scanning first estimates candidate count and required disk space. It then streams address plus initial value directly to a baseline snapshot. Large captures remain disk-backed.

Before starting:

- Confirm sufficient free space under `%LOCALAPPDATA%\MemoryInspector`.
- Use alignment whenever possible.
- Reduce the region set to committed readable memory.
- Keep the 512 MiB default cache budget unless the machine has a reasoned alternative.

## Next scan comparisons

Next Scan reads only addresses from the previous snapshot and supports:

- Exact
- Changed / Unchanged
- Increased / Decreased
- Greater / Less

Signed and unsigned comparisons preserve their numeric meaning. Float and Double comparisons use configured tolerance; NaN and Infinity follow explicit matcher rules.

Failed or partial reads are excluded and summarized. Warning details are bounded so a failing target cannot consume unbounded UI memory.

## Duration filters

Duration filtering can compare endpoints or continuously observe values for Changed, Unchanged, Increased, or Decreased behavior. It supports pause, resume, cancellation, progress, and a final pending result.

The process may change quickly. Choose an observation duration and interval that match the behavior being investigated without overwhelming the target or UI.

## Storage selection

Default policy:

| Candidate count | Strategy |
|---:|---|
| Below 100,000 | Memory-preferred warm cache |
| 100,000–999,999 | Disk snapshot with lazy cache |
| 1,000,000 or more | Disk-backed paging |

Disk remains authoritative. Cache eviction never deletes the snapshot.

## Cancellation and target exit

Cancellation removes incomplete temporary files and does not commit a pending snapshot. If the process exits, the session changes, or its identity no longer matches, scanning stops before commit. A failed scan does not replace the active round.

## Recommended workflow

1. Start Monitoring on a known, authorized target.
2. Narrow readable regions.
3. Choose the correct value type and alignment.
4. Run an exact scan when the value is known; otherwise capture an unknown baseline.
5. Change one behavior in the target.
6. Run a focused Next Scan.
7. Review the pending summary, then Keep or Discard.
8. Pin meaningful branches before temporary cleanup.

