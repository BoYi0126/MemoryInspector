using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MemoryInspector.Application.Monitoring;
using MemoryInspector.Common;
using MemoryInspector.Core.Monitoring;

namespace MemoryInspector.Application.Memory;

public sealed class MemoryReaderService(
    IMonitoringSessionService monitoringSessionService,
    IMemoryReaderProvider provider) : IMemoryReaderService
{
    private readonly IMonitoringSessionService _monitoringSessionService =
        Guard.NotNull(monitoringSessionService);
    private readonly IMemoryReaderProvider _provider = Guard.NotNull(provider);

    public async Task<Result<MemoryReadResult>> ReadAsync(
        ulong address,
        int length,
        MemoryReadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var requestResult = CreateRequest(address, length);

        if (requestResult.IsFailure)
        {
            return Result<MemoryReadResult>.Failure(requestResult.Error);
        }

        var sessionResult = GetConnectedSession();

        if (sessionResult.IsFailure)
        {
            return Result<MemoryReadResult>.Failure(sessionResult.Error);
        }

        var session = sessionResult.Value;
        var result = await _provider.ReadAsync(
            session.Identity,
            requestResult.Value,
            options ?? new MemoryReadOptions(),
            cancellationToken);

        return IsCurrent(session)
            ? result
            : Result<MemoryReadResult>.Failure(SessionChangedError());
    }

    public async Task<Result<T>> TryReadAsync<T>(
        ulong address,
        MemoryReadOptions? options = null,
        CancellationToken cancellationToken = default)
        where T : unmanaged
    {
        var size = Unsafe.SizeOf<T>();
        var readResult = await ReadAsync(
            address,
            size,
            options,
            cancellationToken);

        if (readResult.IsFailure)
        {
            return Result<T>.Failure(readResult.Error);
        }

        if (!readResult.Value.IsComplete)
        {
            var cause = readResult.Value.Warnings.FirstOrDefault();
            var error = new Error(
                ErrorCode.NativeApi,
                $"A complete {typeof(T).Name} value could not be read.");

            return Result<T>.Failure(
                cause is null ? error : error.WithCause(cause));
        }

        return Result<T>.Success(
            MemoryMarshal.Read<T>(readResult.Value.Data.Span));
    }

    public async Task<Result<MemoryBatchReadResult>> ReadBatchAsync(
        IEnumerable<MemoryReadRequest> requests,
        MemoryReadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (requests is null)
        {
            return Result<MemoryBatchReadResult>.Failure(
                new Error(
                    ErrorCode.Validation,
                    "Batch read requests are required."));
        }

        MemoryReadRequest[] requestArray;

        try
        {
            requestArray = requests.ToArray();
        }
        catch (Exception exception)
        {
            return Result<MemoryBatchReadResult>.Failure(
                new Error(
                    ErrorCode.Validation,
                    "Batch read requests could not be evaluated.",
                    exception));
        }

        if (requestArray.Any(request => request is null))
        {
            return Result<MemoryBatchReadResult>.Failure(
                new Error(
                    ErrorCode.Validation,
                    "Batch read requests cannot contain null."));
        }

        var sessionResult = GetConnectedSession();

        if (sessionResult.IsFailure)
        {
            return Result<MemoryBatchReadResult>.Failure(
                sessionResult.Error);
        }

        var session = sessionResult.Value;
        var result = await _provider.ReadBatchAsync(
            session.Identity,
            requestArray,
            options ?? new MemoryReadOptions(),
            cancellationToken);

        return IsCurrent(session)
            ? result
            : Result<MemoryBatchReadResult>.Failure(
                SessionChangedError());
    }

    private Result<MonitoringSession> GetConnectedSession()
    {
        var session = _monitoringSessionService.CurrentSession;

        return session?.State == MonitoringSessionState.Connected
            ? Result<MonitoringSession>.Success(session)
            : Result<MonitoringSession>.Failure(
                new Error(
                    ErrorCode.InvalidState,
                    "A connected monitoring session is required."));
    }

    private bool IsCurrent(MonitoringSession expected)
    {
        var current = _monitoringSessionService.CurrentSession;

        return current?.SessionId == expected.SessionId &&
               current.State == MonitoringSessionState.Connected &&
               current.Identity == expected.Identity;
    }

    private static Result<MemoryReadRequest> CreateRequest(
        ulong address,
        int length)
    {
        try
        {
            return Result<MemoryReadRequest>.Success(
                new MemoryReadRequest(address, length));
        }
        catch (Exception exception)
            when (exception is
                ArgumentOutOfRangeException or
                OverflowException)
        {
            return Result<MemoryReadRequest>.Failure(
                new Error(
                    ErrorCode.Validation,
                    "The memory read address or length is invalid.",
                    exception));
        }
    }

    private static Error SessionChangedError()
    {
        return new Error(
            ErrorCode.InvalidState,
            "The monitoring session changed during the memory read.");
    }
}
