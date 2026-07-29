using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using MemoryInspector.Application.Memory;
using MemoryInspector.Common;
using MemoryInspector.Core.Monitoring;
using MemoryInspector.Core.Processes;
using MemoryInspector.Windows.Memory;
using MemoryInspector.Windows.Tests.Configuration;

namespace MemoryInspector.Windows.Tests.Memory;

[TestClass]
public sealed class WindowsMemoryRegionProviderTests
{
    private static readonly MonitoringSessionIdentity Identity = new(
        42,
        new DateTimeOffset(2026, 7, 29, 8, 30, 0, TimeSpan.Zero),
        ProcessArchitecture.X64,
        "Target");

    [TestMethod]
    public async Task EnumeratesRegionsInAscendingAddressOrder()
    {
        var nativeApi = new FakeMemoryRegionNativeApi(
            maximumApplicationAddress: 0x2FFF,
            QuerySuccess(CreateNative(0, 0x1000)),
            QuerySuccess(CreateNative(0x1000, 0x2000)));
        var provider = CreateProvider(nativeApi);

        var result = await provider.GetRegionsAsync(Identity);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsFalse(result.Value.IsPartial);
        Assert.AreEqual(2, result.Value.Regions.Count);
        CollectionAssert.AreEqual(
            new ulong[] { 0, 0x1000 },
            result.Value.Regions
                .Select(region => region.BaseAddress)
                .ToArray());
        CollectionAssert.AreEqual(
            new ulong[] { 0, 0x1000 },
            nativeApi.QueriedAddresses.ToArray());
        Assert.IsTrue(nativeApi.LastHandle!.IsClosed);
    }

    [TestMethod]
    public async Task FailureAfterValidRegionsReturnsPartialResultAndWarning()
    {
        var nativeApi = new FakeMemoryRegionNativeApi(
            maximumApplicationAddress: 0x2FFF,
            QuerySuccess(CreateNative(0, 0x1000)),
            QueryFailure(299));
        var logger = new RecordingLogger();
        var provider = CreateProvider(nativeApi, logger: logger);

        var result = await provider.GetRegionsAsync(Identity);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(result.Value.IsPartial);
        Assert.AreEqual(1, result.Value.Regions.Count);
        Assert.AreEqual(1, result.Value.Warnings.Count);
        Assert.AreEqual(ErrorCode.NativeApi, result.Value.Warnings[0].Code);
        Assert.IsTrue(
            logger.Entries.Any(entry =>
                entry.Level ==
                MemoryInspector.Application.Logging.AppLogLevel.Warning));
    }

    [TestMethod]
    public async Task FirstAccessDeniedFailureReturnsFailureResult()
    {
        var nativeApi = new FakeMemoryRegionNativeApi(
            maximumApplicationAddress: 0x2FFF,
            QueryFailure(NativeMemoryConstants.ErrorAccessDenied));
        var provider = CreateProvider(nativeApi);

        var result = await provider.GetRegionsAsync(Identity);

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(ErrorCode.AccessDenied, result.Error.Code);
        Assert.IsTrue(nativeApi.LastHandle!.IsClosed);
    }

    [TestMethod]
    public async Task IdentityFailurePreventsOpeningAProcessHandle()
    {
        var nativeApi = new FakeMemoryRegionNativeApi(
            maximumApplicationAddress: 0x2FFF);
        var validator = new DelegateIdentityValidator(
            Result.Failure(
                new Error(
                    ErrorCode.InvalidState,
                    "Identity changed.")));
        var provider = CreateProvider(
            nativeApi,
            identityValidator: validator);

        var result = await provider.GetRegionsAsync(Identity);

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(ErrorCode.InvalidState, result.Error.Code);
        Assert.AreEqual(0, nativeApi.OpenCount);
    }

    [TestMethod]
    public async Task PreCancelledQueryReturnsCancelledResult()
    {
        var nativeApi = new FakeMemoryRegionNativeApi(
            maximumApplicationAddress: 0x2FFF);
        var provider = CreateProvider(nativeApi);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await provider.GetRegionsAsync(
            Identity,
            cancellation.Token);

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(ErrorCode.Cancelled, result.Error.Code);
        Assert.AreEqual(0, nativeApi.OpenCount);
    }

    [TestMethod]
    public async Task LiveProviderEnumeratesCurrentProcessMemoryMap()
    {
        using var process = Process.GetCurrentProcess();
        var identity = CreateIdentity(process);
        var provider = new WindowsMemoryRegionProvider(
            new RecordingLogger());

        var result = await provider.GetRegionsAsync(identity);

        Assert.IsTrue(
            result.IsSuccess,
            result.IsFailure ? result.Error.ToDisplayMessage() : null);
        Assert.IsTrue(result.Value.Regions.Count > 0);
        Assert.IsTrue(
            result.Value.Regions.All(region =>
                region.Size > 0 &&
                region.EndAddress > region.BaseAddress));
        Assert.IsTrue(
            result.Value.Regions.Any(region =>
                region.State ==
                MemoryInspector.Core.Memory.MemoryRegionState.Committed));
    }

    private static WindowsMemoryRegionProvider CreateProvider(
        IMemoryRegionNativeApi nativeApi,
        IProcessIdentityValidator? identityValidator = null,
        RecordingLogger? logger = null)
    {
        return new WindowsMemoryRegionProvider(
            nativeApi,
            identityValidator ??
                new DelegateIdentityValidator(Result.Success()),
            logger ?? new RecordingLogger());
    }

    private static MonitoringSessionIdentity CreateIdentity(Process process)
    {
        return new MonitoringSessionIdentity(
            process.Id,
            new DateTimeOffset(process.StartTime),
            RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X86 => ProcessArchitecture.X86,
                Architecture.X64 => ProcessArchitecture.X64,
                Architecture.Arm => ProcessArchitecture.Arm32,
                Architecture.Arm64 => ProcessArchitecture.Arm64,
                _ => throw new PlatformNotSupportedException(),
            },
            process.ProcessName);
    }

    private static NativeMemoryRegion CreateNative(
        ulong baseAddress,
        ulong size)
    {
        return new NativeMemoryRegion(
            baseAddress,
            baseAddress,
            size,
            NativeMemoryConstants.MemCommit,
            NativeMemoryConstants.PageReadWrite,
            NativeMemoryConstants.MemPrivate);
    }

    private static QueryResponse QuerySuccess(
        NativeMemoryRegion region)
    {
        return new QueryResponse(true, region, 0);
    }

    private static QueryResponse QueryFailure(int errorCode)
    {
        return new QueryResponse(false, default, errorCode);
    }

    private readonly record struct QueryResponse(
        bool Success,
        NativeMemoryRegion Region,
        int ErrorCode);

    private sealed class FakeMemoryRegionNativeApi(
        ulong maximumApplicationAddress,
        params QueryResponse[] responses) : IMemoryRegionNativeApi
    {
        private readonly Queue<QueryResponse> _responses = new(responses);

        public ulong MaximumApplicationAddress { get; } =
            maximumApplicationAddress;

        public List<ulong> QueriedAddresses { get; } = [];

        public SafeProcessHandle? LastHandle { get; private set; }

        public int OpenCount { get; private set; }

        public SafeProcessHandle OpenProcess(int processId)
        {
            OpenCount++;
            LastHandle = new SafeProcessHandle(
                new IntPtr(123),
                ownsHandle: false);
            return LastHandle;
        }

        public bool TryQuery(
            SafeProcessHandle processHandle,
            ulong address,
            out NativeMemoryRegion region,
            out int errorCode)
        {
            QueriedAddresses.Add(address);
            var response = _responses.Count > 0
                ? _responses.Dequeue()
                : QueryFailure(
                    NativeMemoryConstants.ErrorInvalidParameter);
            region = response.Region;
            errorCode = response.ErrorCode;
            return response.Success;
        }
    }

    private sealed class DelegateIdentityValidator(Result result)
        : IProcessIdentityValidator
    {
        public Result Validate(MonitoringSessionIdentity identity)
        {
            return result;
        }
    }
}
