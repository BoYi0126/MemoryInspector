using MemoryInspector.Application.Scanning.Snapshots;
using MemoryInspector.Common;

namespace MemoryInspector.Application.Scanning.Results;

public interface IResultGridService
{
    Task<Result<PagedResult<ResultGridItem>>> LoadPageAsync(
        SnapshotDescriptor snapshot,
        long pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);
}
