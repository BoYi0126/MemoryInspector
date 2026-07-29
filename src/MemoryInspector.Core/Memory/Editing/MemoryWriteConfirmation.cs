using MemoryInspector.Core.Monitoring;
using MemoryInspector.Core.Scanning;

namespace MemoryInspector.Core.Memory.Editing;

public sealed record MemoryWriteConfirmation(
    MonitoringSessionIdentity TargetIdentity,
    ulong Address,
    string? RegionSummary,
    ScanValueType ValueType,
    string OriginalValue,
    string NewValue,
    string OriginalBytes,
    string NewBytes,
    bool VerifyAfterWrite);
