using MemoryInspector.Common;
using MemoryInspector.Core.Memory.Editing;

namespace MemoryInspector.Application.Memory.Editing;

public sealed class NoOpMemoryWriter(
    TimeProvider timeProvider) : IMemoryWriter
{
    private readonly TimeProvider _timeProvider =
        Guard.NotNull(timeProvider);

    public Task<MemoryWriteResult> WriteAsync(
        MemoryWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var original = request.ExpectedOriginalValue ??
            new byte[request.ParsedBytes.Length];
        return Task.FromResult(
            MemoryWriteResultFactory.Succeeded(
                request,
                original,
                request.VerifyAfterWrite
                    ? request.ParsedBytes
                    : null,
                _timeProvider.GetUtcNow()));
    }
}
