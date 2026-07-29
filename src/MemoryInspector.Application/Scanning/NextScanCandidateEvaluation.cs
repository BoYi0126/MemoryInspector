using MemoryInspector.Common;
using MemoryInspector.Core.Scanning;

namespace MemoryInspector.Application.Scanning;

public readonly record struct NextScanCandidateEvaluation(
    CandidateAddress Candidate,
    ReadOnlyMemory<byte> PreviousValue,
    ReadOnlyMemory<byte> CurrentValue,
    bool IsMatch,
    NextScanReadStatus ReadStatus,
    Error? ReadError);
