using MemoryInspector.Common;
using MemoryInspector.Core.Memory.Editing;

namespace MemoryInspector.Application.Memory.Editing;

public sealed class InMemoryMemoryWriteAuditService :
    IMemoryWriteAuditService
{
    private readonly object _sync = new();
    private readonly List<MemoryWriteAuditEntry> _entries = [];

    public Task<Result> RecordAsync(
        MemoryWriteAuditEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            _entries.Add(entry);
        }

        return Task.FromResult(Result.Success());
    }

    public Task<Result<IReadOnlyList<MemoryWriteAuditEntry>>>
        ReadRecentAsync(
            int maximumCount = 1_000,
            CancellationToken cancellationToken = default)
    {
        if (maximumCount <= 0)
        {
            return Task.FromResult(
                Result<IReadOnlyList<MemoryWriteAuditEntry>>.Failure(
                    new Error(
                        ErrorCode.Validation,
                        "Audit maximum count must be greater than zero.")));
        }

        cancellationToken.ThrowIfCancellationRequested();
        MemoryWriteAuditEntry[] entries;

        lock (_sync)
        {
            entries = _entries
                .TakeLast(maximumCount)
                .Reverse()
                .ToArray();
        }

        return Task.FromResult(
            Result<IReadOnlyList<MemoryWriteAuditEntry>>.Success(
                Array.AsReadOnly(entries)));
    }
}
