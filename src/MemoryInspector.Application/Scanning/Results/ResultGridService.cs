using MemoryInspector.Application.Scanning.Snapshots;
using MemoryInspector.Common;

namespace MemoryInspector.Application.Scanning.Results;

public sealed class ResultGridService(
    ISnapshotStorage snapshotStorage) : IResultGridService
{
    private readonly ISnapshotStorage _snapshotStorage =
        Guard.NotNull(snapshotStorage);

    public async Task<Result<PagedResult<ResultGridItem>>>
        LoadPageAsync(
            SnapshotDescriptor snapshot,
            long pageNumber,
            int pageSize,
            CancellationToken cancellationToken = default)
    {
        if (snapshot is null)
        {
            return Validation(
                "A snapshot descriptor is required.");
        }

        var pageResult = await _snapshotStorage.ReadPageAsync(
                snapshot,
                pageNumber,
                pageSize,
                cancellationToken)
            .ConfigureAwait(false);

        if (pageResult.IsFailure)
        {
            return Result<PagedResult<ResultGridItem>>.Failure(
                pageResult.Error);
        }

        var items = pageResult.Value.Items
            .Select(record =>
                new ResultGridItem(
                    record.Candidate.Address,
                    snapshot.ValueType,
                    record.Value.Span,
                    snapshot.IncludesValues
                        ? ResultReadStatus.Available
                        : ResultReadStatus.AddressOnly))
            .ToArray();

        return Result<PagedResult<ResultGridItem>>.Success(
            new PagedResult<ResultGridItem>(
                items,
                pageResult.Value.PageNumber,
                pageResult.Value.PageSize,
                pageResult.Value.TotalCount));
    }

    private static Result<PagedResult<ResultGridItem>>
        Validation(string message)
    {
        return Result<PagedResult<ResultGridItem>>.Failure(
            new Error(ErrorCode.Validation, message));
    }
}
