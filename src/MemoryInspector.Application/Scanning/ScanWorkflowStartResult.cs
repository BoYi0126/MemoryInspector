using MemoryInspector.Application.Scanning.Snapshots;
using MemoryInspector.Common;

namespace MemoryInspector.Application.Scanning;

public sealed record ScanWorkflowStartResult(
    SnapshotDescriptor Snapshot,
    FilterPipelineState PipelineState,
    IReadOnlyList<Error> Warnings,
    bool IsPartial);
