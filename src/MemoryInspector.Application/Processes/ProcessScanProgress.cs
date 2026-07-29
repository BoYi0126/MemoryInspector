namespace MemoryInspector.Application.Processes;

public readonly record struct ProcessScanProgress(
    int ScannedCount,
    int? TotalCount);
