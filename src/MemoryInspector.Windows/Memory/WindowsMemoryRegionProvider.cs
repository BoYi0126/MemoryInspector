using System.ComponentModel;
using MemoryInspector.Application.Logging;
using MemoryInspector.Application.Memory;
using MemoryInspector.Common;
using MemoryInspector.Core.Memory;
using MemoryInspector.Core.Monitoring;

namespace MemoryInspector.Windows.Memory;

public sealed class WindowsMemoryRegionProvider : IMemoryRegionProvider
{
    private readonly IMemoryRegionNativeApi _nativeApi;
    private readonly IProcessIdentityValidator _identityValidator;
    private readonly IAppLogger _logger;

    public WindowsMemoryRegionProvider(IAppLogger logger)
        : this(
            new WindowsMemoryRegionNativeApi(),
            new WindowsProcessIdentityValidator(),
            logger)
    {
    }

    internal WindowsMemoryRegionProvider(
        IMemoryRegionNativeApi nativeApi,
        IProcessIdentityValidator identityValidator,
        IAppLogger logger)
    {
        _nativeApi = nativeApi ??
            throw new ArgumentNullException(nameof(nativeApi));
        _identityValidator = identityValidator ??
            throw new ArgumentNullException(nameof(identityValidator));
        _logger = Guard.NotNull(logger);
    }

    public async Task<Result<MemoryRegionQueryResult>> GetRegionsAsync(
        MonitoringSessionIdentity identity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);

        try
        {
            return await Task.Run(
                () => QueryRegions(identity, cancellationToken),
                cancellationToken);
        }
        catch (OperationCanceledException exception)
            when (cancellationToken.IsCancellationRequested)
        {
            return Result<MemoryRegionQueryResult>.Failure(
                new Error(
                    ErrorCode.Cancelled,
                    "Memory region enumeration was cancelled.",
                    exception));
        }
        catch (Win32Exception exception)
            when (exception.NativeErrorCode ==
                  NativeMemoryConstants.ErrorAccessDenied)
        {
            return Result<MemoryRegionQueryResult>.Failure(
                new Error(
                    ErrorCode.AccessDenied,
                    "Access to the target memory map was denied.",
                    exception));
        }
        catch (Exception exception) when (
            exception is Win32Exception or
            OverflowException or
            NotSupportedException or
            PlatformNotSupportedException)
        {
            return Result<MemoryRegionQueryResult>.Failure(
                new Error(
                    ErrorCode.NativeApi,
                    "The target memory map could not be enumerated.",
                    exception));
        }
    }

    private Result<MemoryRegionQueryResult> QueryRegions(
        MonitoringSessionIdentity identity,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var validation = _identityValidator.Validate(identity);

        if (validation.IsFailure)
        {
            return Result<MemoryRegionQueryResult>.Failure(validation.Error);
        }

        using var processHandle =
            _nativeApi.OpenProcess(identity.ProcessId);
        var maximumAddress = _nativeApi.MaximumApplicationAddress;
        var regions = new List<MemoryRegion>();
        ulong address = 0;

        while (address <= maximumAddress)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_nativeApi.TryQuery(
                processHandle,
                address,
                out var nativeRegion,
                out var errorCode))
            {
                return CompleteAfterQueryFailure(regions, errorCode);
            }

            MemoryRegion region;

            try
            {
                region = MemoryRegionMapper.Map(nativeRegion);
            }
            catch (Exception exception)
                when (exception is
                    ArgumentOutOfRangeException or
                    OverflowException)
            {
                return CompleteAfterInvalidRegion(
                    regions,
                    address,
                    exception);
            }

            if (region.EndAddress <= address)
            {
                return CompleteAfterInvalidRegion(
                    regions,
                    address,
                    new InvalidOperationException(
                        "VirtualQueryEx did not advance the query address."));
            }

            regions.Add(region);
            address = region.EndAddress;
        }

        return Result<MemoryRegionQueryResult>.Success(
            new MemoryRegionQueryResult(regions));
    }

    private Result<MemoryRegionQueryResult> CompleteAfterQueryFailure(
        IReadOnlyList<MemoryRegion> regions,
        int errorCode)
    {
        var exception = new Win32Exception(errorCode);
        var error = new Error(
            errorCode == NativeMemoryConstants.ErrorAccessDenied
                ? ErrorCode.AccessDenied
                : ErrorCode.NativeApi,
            $"Memory region enumeration stopped at region " +
            $"{regions.Count:N0}.",
            exception);

        return CompleteOrFail(regions, error);
    }

    private Result<MemoryRegionQueryResult> CompleteAfterInvalidRegion(
        IReadOnlyList<MemoryRegion> regions,
        ulong address,
        Exception exception)
    {
        var error = new Error(
            ErrorCode.NativeApi,
            $"Memory region enumeration encountered invalid data at " +
            $"0x{address:X}.",
            exception);

        return CompleteOrFail(regions, error);
    }

    private Result<MemoryRegionQueryResult> CompleteOrFail(
        IReadOnlyList<MemoryRegion> regions,
        Error error)
    {
        if (regions.Count == 0)
        {
            return Result<MemoryRegionQueryResult>.Failure(error);
        }

        _ = _logger.Log(
            AppLogLevel.Warning,
            error.ToDisplayMessage(),
            error.Exception);

        return Result<MemoryRegionQueryResult>.Success(
            new MemoryRegionQueryResult(regions, [error]));
    }
}
