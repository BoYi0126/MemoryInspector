using MemoryInspector.Common;
using MemoryInspector.Core.Scanning;

namespace MemoryInspector.Application.Scanning;

public sealed class FirstScanResult
{
    private readonly CandidateAddress[] _candidates;

    public FirstScanResult(
        ScanResult summary,
        IEnumerable<CandidateAddress> candidates,
        IEnumerable<Error>? warnings,
        int scannedRegionCount,
        int skippedRegionCount,
        bool isResultLimitReached)
    {
        Summary = summary ??
            throw new ArgumentNullException(nameof(summary));
        ArgumentNullException.ThrowIfNull(candidates);

        _candidates = candidates.ToArray();
        Warnings = Array.AsReadOnly(
            warnings?.ToArray() ?? Array.Empty<Error>());

        if (_candidates.LongLength != summary.CandidateCount)
        {
            throw new ArgumentException(
                "Candidate count must match the scan summary.",
                nameof(candidates));
        }

        if (Warnings.Any(error =>
            error is null || error.Code == ErrorCode.None))
        {
            throw new ArgumentException(
                "A scan warning must describe a failure.",
                nameof(warnings));
        }

        if (scannedRegionCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(scannedRegionCount));
        }

        if (skippedRegionCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(skippedRegionCount));
        }

        ScannedRegionCount = scannedRegionCount;
        SkippedRegionCount = skippedRegionCount;
        IsResultLimitReached = isResultLimitReached;
    }

    public ScanResult Summary { get; }

    public ReadOnlyMemory<CandidateAddress> Candidates => _candidates;

    public IReadOnlyList<Error> Warnings { get; }

    public int ScannedRegionCount { get; }

    public int SkippedRegionCount { get; }

    public bool IsResultLimitReached { get; }
}
