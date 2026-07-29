using System.ComponentModel;
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
public sealed class WindowsMemoryReaderProviderTests
{
    private static readonly MonitoringSessionIdentity Identity = new(
        42,
        new DateTimeOffset(2026, 7, 29, 8, 30, 0, TimeSpan.Zero),
        ProcessArchitecture.X64,
        "Target");

    [TestMethod]
    public async Task ChunkedReadUsesConfiguredAddressesAndCombinesData()
    {
        var nativeApi = new FakeMemoryReaderNativeApi(
            ReadSuccess(1, 2, 3, 4),
            ReadSuccess(5, 6, 7, 8),
            ReadSuccess(9, 10));
        var provider = CreateProvider(nativeApi);

        var result = await provider.ReadAsync(
            Identity,
            new MemoryReadRequest(0x1000, 10),
            new MemoryReadOptions(4));

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(result.Value.IsComplete);
        CollectionAssert.AreEqual(
            Enumerable.Range(1, 10)
                .Select(value => (byte)value)
                .ToArray(),
            result.Value.Data.ToArray());
        CollectionAssert.AreEqual(
            new ulong[] { 0x1000, 0x1004, 0x1008 },
            nativeApi.Calls.Select(call => call.Address).ToArray());
        CollectionAssert.AreEqual(
            new[] { 4, 4, 2 },
            nativeApi.Calls.Select(call => call.Length).ToArray());
        Assert.IsTrue(nativeApi.LastHandle!.IsClosed);
    }

    [TestMethod]
    public async Task PartialReadReturnsAvailableBytesAndWarning()
    {
        var nativeApi = new FakeMemoryReaderNativeApi(
            ReadSuccess(1, 2, 3, 4),
            new NativeReadResponse(
                Success: false,
                Data: new byte[] { 5, 6 },
                BytesRead: 2,
                ErrorCode: NativeMemoryConstants.ErrorPartialCopy));
        var logger = new RecordingLogger();
        var provider = CreateProvider(nativeApi, logger: logger);

        var result = await provider.ReadAsync(
            Identity,
            new MemoryReadRequest(0x1000, 8),
            new MemoryReadOptions(4));

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(result.Value.IsPartial);
        Assert.AreEqual(6, result.Value.BytesRead);
        CollectionAssert.AreEqual(
            new byte[] { 1, 2, 3, 4, 5, 6 },
            result.Value.Data.ToArray());
        Assert.AreEqual(1, result.Value.Warnings.Count);
        Assert.IsTrue(
            logger.Entries.Any(entry =>
                entry.Level ==
                MemoryInspector.Application.Logging.AppLogLevel.Warning));
    }

    [TestMethod]
    public async Task InvalidAddressReturnsFailureResult()
    {
        var nativeApi = new FakeMemoryReaderNativeApi(
            ReadFailure(NativeMemoryConstants.ErrorInvalidAddress));
        var provider = CreateProvider(nativeApi);

        var result = await provider.ReadAsync(
            Identity,
            new MemoryReadRequest(0xDEAD_BEEF, 4),
            new MemoryReadOptions());

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(ErrorCode.NotFound, result.Error.Code);
        Assert.IsTrue(nativeApi.LastHandle!.IsClosed);
    }

    [TestMethod]
    public async Task BatchReadValidatesOnceAndSharesOneHandle()
    {
        var nativeApi = new FakeMemoryReaderNativeApi(
            ReadSuccess(1, 2),
            ReadFailure(NativeMemoryConstants.ErrorNoAccess),
            ReadSuccess(3, 4));
        var validator = new RecordingIdentityValidator();
        var provider = CreateProvider(
            nativeApi,
            validator);
        var requests = new[]
        {
            new MemoryReadRequest(0x1000, 2),
            new MemoryReadRequest(0x2000, 2),
            new MemoryReadRequest(0x3000, 2),
        };

        var result = await provider.ReadBatchAsync(
            Identity,
            requests,
            new MemoryReadOptions());

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(2, result.Value.SucceededCount);
        Assert.AreEqual(1, result.Value.FailedCount);
        Assert.IsTrue(result.Value.IsPartial);
        Assert.AreEqual(1, validator.CallCount);
        Assert.AreEqual(1, nativeApi.OpenCount);
        Assert.IsTrue(nativeApi.LastHandle!.IsClosed);
    }

    [TestMethod]
    public async Task IdentityFailurePreventsOpeningHandle()
    {
        var nativeApi = new FakeMemoryReaderNativeApi();
        var validator = new RecordingIdentityValidator(
            Result.Failure(
                new Error(
                    ErrorCode.InvalidState,
                    "Identity changed.")));
        var provider = CreateProvider(nativeApi, validator);

        var result = await provider.ReadAsync(
            Identity,
            new MemoryReadRequest(0x1000, 4),
            new MemoryReadOptions());

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(ErrorCode.InvalidState, result.Error.Code);
        Assert.AreEqual(0, nativeApi.OpenCount);
    }

    [TestMethod]
    public async Task PreCancelledReadReturnsCancelledResult()
    {
        var nativeApi = new FakeMemoryReaderNativeApi();
        var provider = CreateProvider(nativeApi);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await provider.ReadAsync(
            Identity,
            new MemoryReadRequest(0x1000, 4),
            new MemoryReadOptions(),
            cancellation.Token);

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(ErrorCode.Cancelled, result.Error.Code);
        Assert.AreEqual(0, nativeApi.OpenCount);
    }

    [TestMethod]
    public async Task OpenAccessDeniedReturnsAccessDeniedResult()
    {
        var nativeApi = new FakeMemoryReaderNativeApi
        {
            OpenException = new Win32Exception(
                NativeMemoryConstants.ErrorAccessDenied),
        };
        var provider = CreateProvider(nativeApi);

        var result = await provider.ReadAsync(
            Identity,
            new MemoryReadRequest(0x1000, 4),
            new MemoryReadOptions());

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(ErrorCode.AccessDenied, result.Error.Code);
    }

    [TestMethod]
    public async Task LiveProviderReadsAllocatedCurrentProcessMemory()
    {
        var expected = new byte[]
        {
            0x10, 0x20, 0x30, 0x40,
            0x50, 0x60, 0x70, 0x80,
        };
        var memory = Marshal.AllocHGlobal(expected.Length);

        try
        {
            Marshal.Copy(expected, 0, memory, expected.Length);
            using var process = Process.GetCurrentProcess();
            var identity = CreateIdentity(process);
            var provider = new WindowsMemoryReaderProvider(
                new RecordingLogger());

            var result = await provider.ReadAsync(
                identity,
                new MemoryReadRequest(
                    unchecked((ulong)memory.ToInt64()),
                    expected.Length),
                new MemoryReadOptions(3));

            Assert.IsTrue(
                result.IsSuccess,
                result.IsFailure
                    ? result.Error.ToDisplayMessage()
                    : null);
            Assert.IsTrue(result.Value.IsComplete);
            CollectionAssert.AreEqual(
                expected,
                result.Value.Data.ToArray());
        }
        finally
        {
            Marshal.FreeHGlobal(memory);
        }
    }

    private static WindowsMemoryReaderProvider CreateProvider(
        IMemoryReaderNativeApi nativeApi,
        IProcessIdentityValidator? validator = null,
        RecordingLogger? logger = null)
    {
        return new WindowsMemoryReaderProvider(
            nativeApi,
            validator ?? new RecordingIdentityValidator(),
            logger ?? new RecordingLogger());
    }

    private static NativeReadResponse ReadSuccess(
        params byte[] data)
    {
        return new NativeReadResponse(
            Success: true,
            Data: data,
            BytesRead: data.Length,
            ErrorCode: 0);
    }

    private static NativeReadResponse ReadFailure(int errorCode)
    {
        return new NativeReadResponse(
            Success: false,
            Data: Array.Empty<byte>(),
            BytesRead: 0,
            ErrorCode: errorCode);
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

    private readonly record struct NativeReadResponse(
        bool Success,
        byte[] Data,
        int BytesRead,
        int ErrorCode);

    private readonly record struct NativeReadCall(
        ulong Address,
        int Length);

    private sealed class FakeMemoryReaderNativeApi(
        params NativeReadResponse[] responses) : IMemoryReaderNativeApi
    {
        private readonly Queue<NativeReadResponse> _responses =
            new(responses);

        public Exception? OpenException { get; init; }

        public int OpenCount { get; private set; }

        public SafeProcessHandle? LastHandle { get; private set; }

        public List<NativeReadCall> Calls { get; } = [];

        public SafeProcessHandle OpenProcess(int processId)
        {
            OpenCount++;

            if (OpenException is not null)
            {
                throw OpenException;
            }

            LastHandle = new SafeProcessHandle(
                new IntPtr(123),
                ownsHandle: false);
            return LastHandle;
        }

        public bool TryRead(
            SafeProcessHandle processHandle,
            ulong address,
            byte[] buffer,
            out int bytesRead,
            out int errorCode)
        {
            Calls.Add(new NativeReadCall(address, buffer.Length));
            var response = _responses.Count > 0
                ? _responses.Dequeue()
                : ReadFailure(
                    NativeMemoryConstants.ErrorInvalidAddress);
            var copyLength = Math.Min(
                Math.Min(response.BytesRead, response.Data.Length),
                buffer.Length);
            Array.Copy(response.Data, buffer, copyLength);
            bytesRead = response.BytesRead;
            errorCode = response.ErrorCode;
            return response.Success;
        }
    }

    private sealed class RecordingIdentityValidator(
        Result? result = null) : IProcessIdentityValidator
    {
        public int CallCount { get; private set; }

        public Result Validate(MonitoringSessionIdentity identity)
        {
            CallCount++;
            return result ?? Result.Success();
        }
    }
}
