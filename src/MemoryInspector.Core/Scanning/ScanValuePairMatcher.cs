namespace MemoryInspector.Core.Scanning;

public delegate bool ScanValuePairMatcher(
    ReadOnlySpan<byte> currentValue,
    ReadOnlySpan<byte> comparisonValue);
