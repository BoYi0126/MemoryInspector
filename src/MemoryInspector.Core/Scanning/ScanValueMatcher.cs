namespace MemoryInspector.Core.Scanning;

public delegate bool ScanValueMatcher(
    ReadOnlySpan<byte> currentValue);
