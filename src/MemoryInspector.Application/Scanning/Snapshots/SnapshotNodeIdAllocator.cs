using MemoryInspector.Common;

namespace MemoryInspector.Application.Scanning.Snapshots;

public sealed class SnapshotNodeIdAllocator(
    ISnapshotStorage snapshotStorage) : ISnapshotNodeIdAllocator
{
    private readonly ISnapshotStorage _snapshotStorage =
        Guard.NotNull(snapshotStorage);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<Guid, int> _nextBySession = [];
    private readonly Dictionary<Guid, HashSet<int>> _reservedBySession = [];

    public async Task<Result<int>> ReserveAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        if (sessionId == Guid.Empty)
        {
            return Result<int>.Failure(
                new Error(
                    ErrorCode.Validation,
                    "Session ID cannot be empty."));
        }

        try
        {
            await _gate.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
        {
            return Result<int>.Failure(
                new Error(
                    ErrorCode.Cancelled,
                    "Snapshot node allocation was cancelled.",
                    exception));
        }

        try
        {
            var candidate = _nextBySession.GetValueOrDefault(
                sessionId,
                1);
            var reserved = _reservedBySession.GetValueOrDefault(
                sessionId);

            if (reserved is null)
            {
                reserved = [];
                _reservedBySession[sessionId] = reserved;
            }

            while (candidate > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!reserved.Contains(candidate))
                {
                    var open = await _snapshotStorage.OpenAsync(
                            sessionId,
                            candidate,
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (open.IsFailure &&
                        open.Error.Code == ErrorCode.NotFound)
                    {
                        reserved.Add(candidate);
                        _nextBySession[sessionId] =
                            candidate == int.MaxValue
                                ? int.MinValue
                                : candidate + 1;
                        return Result<int>.Success(candidate);
                    }

                    if (open.IsFailure)
                    {
                        return Result<int>.Failure(open.Error);
                    }
                }

                if (candidate == int.MaxValue)
                {
                    break;
                }

                candidate++;
            }

            return Result<int>.Failure(
                new Error(
                    ErrorCode.ResourceExhausted,
                    "No snapshot node ID remains available."));
        }
        catch (OperationCanceledException exception)
        {
            return Result<int>.Failure(
                new Error(
                    ErrorCode.Cancelled,
                    "Snapshot node allocation was cancelled.",
                    exception));
        }
        finally
        {
            _gate.Release();
        }
    }
}
