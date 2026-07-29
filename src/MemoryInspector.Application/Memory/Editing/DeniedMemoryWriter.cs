using MemoryInspector.Common;
using MemoryInspector.Core.Memory.Editing;

namespace MemoryInspector.Application.Memory.Editing;

public sealed class DeniedMemoryWriter(
    TimeProvider timeProvider) : IMemoryWriter
{
    private readonly TimeProvider _timeProvider =
        Guard.NotNull(timeProvider);

    public Task<MemoryWriteResult> WriteAsync(
        MemoryWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(
                MemoryWriteResultFactory.Failed(
                    request,
                    MemoryWriteFailureReason.Cancelled,
                    new Error(
                        ErrorCode.Cancelled,
                        "The denied write operation was cancelled."),
                    _timeProvider.GetUtcNow()));
        }

        return Task.FromResult(
            MemoryWriteResultFactory.Failed(
                request,
                MemoryWriteFailureReason.WriterDenied,
                new Error(
                    ErrorCode.AccessDenied,
                    "No platform memory writer is enabled. " +
                    "Phase 24 does not call a native write API."),
                _timeProvider.GetUtcNow()));
    }
}
