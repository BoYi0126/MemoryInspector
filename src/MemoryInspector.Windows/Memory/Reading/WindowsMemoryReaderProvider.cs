using System.ComponentModel;
using Microsoft.Win32.SafeHandles;
using MemoryInspector.Application.Logging;
using MemoryInspector.Application.Memory;
using MemoryInspector.Common;
using MemoryInspector.Core.Monitoring;

namespace MemoryInspector.Windows.Memory;

public sealed class WindowsMemoryReaderProvider
    : IMemoryReaderProvider
{
    private readonly IMemoryReaderNativeApi _nativeApi;
    private readonly IProcessIdentityValidator _identityValidator;
    private readonly IAppLogger _logger;

    public WindowsMemoryReaderProvider(IAppLogger logger)
        : this(
            new WindowsMemoryReaderNativeApi(),
            new WindowsProcessIdentityValidator(),
            logger)
    {
    }

    internal WindowsMemoryReaderProvider(
        IMemoryReaderNativeApi nativeApi,
        IProcessIdentityValidator identityValidator,
        IAppLogger logger)
    {
        _nativeApi = nativeApi ??
            throw new ArgumentNullException(nameof(nativeApi));
        _identityValidator = identityValidator ??
            throw new ArgumentNullException(nameof(identityValidator));
        _logger = Guard.NotNull(logger);
    }

    public Task<Result<MemoryReadResult>> ReadAsync(
        MonitoringSessionIdentity identity,
        MemoryReadRequest request,
        MemoryReadOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);

        return ExecuteAsync(
            () => Read(identity, request, options, cancellationToken),
            "Memory read",
            cancellationToken);
    }

    public Task<Result<MemoryBatchReadResult>> ReadBatchAsync(
        MonitoringSessionIdentity identity,
        IReadOnlyList<MemoryReadRequest> requests,
        MemoryReadOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(options);

        return ExecuteAsync(
            () => ReadBatch(
                identity,
                requests,
                options,
                cancellationToken),
            "Batch memory read",
            cancellationToken);
    }

    private Result<MemoryReadResult> Read(
        MonitoringSessionIdentity identity,
        MemoryReadRequest request,
        MemoryReadOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var validation = _identityValidator.Validate(identity);

        if (validation.IsFailure)
        {
            return Result<MemoryReadResult>.Failure(validation.Error);
        }

        using var processHandle =
            _nativeApi.OpenProcess(identity.ProcessId);
        return ReadWithHandle(
            processHandle,
            request,
            options,
            cancellationToken);
    }

    private Result<MemoryBatchReadResult> ReadBatch(
        MonitoringSessionIdentity identity,
        IReadOnlyList<MemoryReadRequest> requests,
        MemoryReadOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (requests.Any(request => request is null))
        {
            return Result<MemoryBatchReadResult>.Failure(
                new Error(
                    ErrorCode.Validation,
                    "Batch read requests cannot contain null."));
        }

        if (requests.Count == 0)
        {
            return Result<MemoryBatchReadResult>.Success(
                new MemoryBatchReadResult(
                    Array.Empty<MemoryBatchReadItem>()));
        }

        var validation = _identityValidator.Validate(identity);

        if (validation.IsFailure)
        {
            return Result<MemoryBatchReadResult>.Failure(
                validation.Error);
        }

        using var processHandle =
            _nativeApi.OpenProcess(identity.ProcessId);
        var items = new List<MemoryBatchReadItem>(requests.Count);

        foreach (var request in requests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = ReadWithHandle(
                processHandle,
                request,
                options,
                cancellationToken);
            items.Add(new MemoryBatchReadItem(request, result));
        }

        return Result<MemoryBatchReadResult>.Success(
            new MemoryBatchReadResult(items));
    }

    private Result<MemoryReadResult> ReadWithHandle(
        SafeProcessHandle processHandle,
        MemoryReadRequest request,
        MemoryReadOptions options,
        CancellationToken cancellationToken)
    {
        byte[] output;

        try
        {
            output = new byte[request.Length];
        }
        catch (OutOfMemoryException exception)
        {
            return Result<MemoryReadResult>.Failure(
                new Error(
                    ErrorCode.ResourceExhausted,
                    $"A {request.Length:N0}-byte read buffer " +
                    "could not be allocated.",
                    exception));
        }

        var totalBytesRead = 0;
        Error? partialWarning = null;

        while (totalBytesRead < request.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chunkLength = Math.Min(
                options.ChunkSizeBytes,
                request.Length - totalBytesRead);
            var chunk = new byte[chunkLength];
            var address = request.Address + (ulong)totalBytesRead;
            var success = _nativeApi.TryRead(
                processHandle,
                address,
                chunk,
                out var bytesRead,
                out var errorCode);

            if (bytesRead < 0 || bytesRead > chunkLength)
            {
                partialWarning = new Error(
                    ErrorCode.NativeApi,
                    $"ReadProcessMemory returned an invalid byte count " +
                    $"at 0x{address:X}.");
                break;
            }

            if (bytesRead > 0)
            {
                Buffer.BlockCopy(
                    chunk,
                    0,
                    output,
                    totalBytesRead,
                    bytesRead);
                totalBytesRead += bytesRead;
            }

            if (!success || bytesRead < chunkLength)
            {
                partialWarning = CreateReadError(
                    address + (ulong)bytesRead,
                    errorCode,
                    partial: totalBytesRead > 0);
                break;
            }
        }

        if (totalBytesRead == 0 && partialWarning is not null)
        {
            return Result<MemoryReadResult>.Failure(partialWarning);
        }

        var data = output.AsSpan(0, totalBytesRead);
        var result = partialWarning is null
            ? new MemoryReadResult(request, data)
            : new MemoryReadResult(request, data, [partialWarning]);

        if (partialWarning is not null)
        {
            _ = _logger.Log(
                AppLogLevel.Warning,
                partialWarning.ToDisplayMessage(),
                partialWarning.Exception);
        }

        return Result<MemoryReadResult>.Success(result);
    }

    private static Error CreateReadError(
        ulong address,
        int nativeErrorCode,
        bool partial)
    {
        var exception = nativeErrorCode == 0
            ? null
            : new Win32Exception(nativeErrorCode);
        var errorCode = nativeErrorCode switch
        {
            NativeMemoryConstants.ErrorAccessDenied =>
                ErrorCode.AccessDenied,
            NativeMemoryConstants.ErrorPartialCopy or
            NativeMemoryConstants.ErrorInvalidAddress or
            NativeMemoryConstants.ErrorNoAccess or
            NativeMemoryConstants.ErrorInvalidParameter =>
                ErrorCode.NotFound,
            _ => ErrorCode.NativeApi,
        };
        var message = partial
            ? $"Memory was only partially read before 0x{address:X}."
            : $"Memory at 0x{address:X} could not be read.";

        return new Error(errorCode, message, exception);
    }

    private static async Task<Result<T>> ExecuteAsync<T>(
        Func<Result<T>> operation,
        string operationName,
        CancellationToken cancellationToken)
    {
        try
        {
            return await Task.Run(operation, cancellationToken);
        }
        catch (OperationCanceledException exception)
            when (cancellationToken.IsCancellationRequested)
        {
            return Result<T>.Failure(
                new Error(
                    ErrorCode.Cancelled,
                    $"{operationName} was cancelled.",
                    exception));
        }
        catch (Win32Exception exception)
            when (exception.NativeErrorCode ==
                  NativeMemoryConstants.ErrorAccessDenied)
        {
            return Result<T>.Failure(
                new Error(
                    ErrorCode.AccessDenied,
                    "Access to the target process memory was denied.",
                    exception));
        }
        catch (OutOfMemoryException exception)
        {
            return Result<T>.Failure(
                new Error(
                    ErrorCode.ResourceExhausted,
                    "The memory read could not allocate its buffer.",
                    exception));
        }
        catch (Exception exception) when (
            exception is Win32Exception or
            OverflowException or
            NotSupportedException or
            PlatformNotSupportedException)
        {
            return Result<T>.Failure(
                new Error(
                    ErrorCode.NativeApi,
                    $"{operationName} failed.",
                    exception));
        }
    }
}
