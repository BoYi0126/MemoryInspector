using MemoryInspector.Core.Scanning;

namespace MemoryInspector.Application.Scanning.Snapshots;

public readonly record struct SnapshotRecord(
    CandidateAddress Candidate,
    ReadOnlyMemory<byte> Value)
{
    public SnapshotRecord(CandidateAddress candidate)
        : this(candidate, ReadOnlyMemory<byte>.Empty)
    {
    }
}
