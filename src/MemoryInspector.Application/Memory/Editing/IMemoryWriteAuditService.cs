using MemoryInspector.Common;
using MemoryInspector.Core.Memory.Editing;

namespace MemoryInspector.Application.Memory.Editing;

public interface IMemoryWriteAuditService
{
    Task<Result> RecordAsync(
        MemoryWriteAuditEntry entry,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<MemoryWriteAuditEntry>>> ReadRecentAsync(
        int maximumCount = 1_000,
        CancellationToken cancellationToken = default);
}
