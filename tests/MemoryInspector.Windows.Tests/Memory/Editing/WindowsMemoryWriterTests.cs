using Microsoft.Win32.SafeHandles;
using MemoryInspector.Application.Memory.Editing;
using MemoryInspector.Application.Monitoring;
using MemoryInspector.Common;
using MemoryInspector.Core.Memory.Editing;
using MemoryInspector.Core.Monitoring;
using MemoryInspector.Core.Processes;
using MemoryInspector.Core.Scanning;
using MemoryInspector.Windows.Memory;
using MemoryInspector.Windows.Memory.Editing;

namespace MemoryInspector.Windows.Tests.Memory.Editing;

[TestClass]
public sealed class WindowsMemoryWriterTests
{
    private static readonly DateTimeOffset Now = new(
        2026,
        7,
        29,
        12,
        0,
        0,
        TimeSpan.Zero);
    private static readonly MonitoringSessionIdentity Identity = new(
        321,
        Now.AddHours(-1),
        ProcessArchitecture.X64,
        "MemoryInspector.TestTarget");

    [TestMethod]
    public async Task WriteUsesOneHandleAndReturnsVerifiedReadBack()
    {
        var native = new FakeNativeApi();
        native.Reads.Enqueue(ReadSuccess(BitConverter.GetBytes(10)));
        native.Reads.Enqueue(ReadSuccess(BitConverter.GetBytes(20)));
        using var writer = CreateWriter(native);

        var result = await writer.WriteAsync(
            CreateRequest(requested: 20, expected: 10));

        Assert.IsTrue(result.Success);
        Assert.AreEqual(1, native.OpenCount);
        Assert.AreEqual(1, native.QueryCount);
        Assert.AreEqual(2, native.ReadCount);
        Assert.AreEqual(1, native.WriteCount);
        Assert.AreEqual(4, result.WrittenByteCount);
        Assert.AreEqual(
            MemoryWriteVerificationStatus.Verified,
            result.Verification.Status);
        Assert.AreEqual(
            20,
            BitConverter.ToInt32(
                result.ReadBackValue!.Value.Span));
        Assert.IsTrue(native.LastHandle!.IsClosed);
    }

    [TestMethod]
    public async Task ExpectedOriginalMismatchStopsBeforeNativeWrite()
    {
        var native = new FakeNativeApi();
        native.Reads.Enqueue(ReadSuccess(BitConverter.GetBytes(10)));
        using var writer = CreateWriter(native);

        var result = await writer.WriteAsync(
            CreateRequest(requested: 20, expected: 11));

        Assert.IsFalse(result.Success);
        Assert.AreEqual(
            MemoryWriteFailureReason.OriginalValueMismatch,
            result.FailureReason);
        Assert.AreEqual(0, native.WriteCount);
        Assert.AreEqual(
            10,
            BitConverter.ToInt32(
                result.OriginalValue!.Value.Span));
    }

    [TestMethod]
    public async Task ReadOnlyRegionIsRejectedBeforeReadingOrWriting()
    {
        var native = new FakeNativeApi
        {
            Region = Region(
                NativeMemoryConstants.PageReadOnly),
        };
        using var writer = CreateWriter(native);

        var result = await writer.WriteAsync(
            CreateRequest(requested: 20));

        Assert.IsFalse(result.Success);
        Assert.AreEqual(
            MemoryWriteFailureReason.RegionNotWritable,
            result.FailureReason);
        Assert.AreEqual(0, native.ReadCount);
        Assert.AreEqual(0, native.WriteCount);
    }

    [TestMethod]
    public async Task GuardPageIsRejectedWithoutChangingProtection()
    {
        var native = new FakeNativeApi
        {
            Region = Region(
                NativeMemoryConstants.PageReadWrite |
                NativeMemoryConstants.PageGuard),
        };
        using var writer = CreateWriter(native);

        var result = await writer.WriteAsync(
            CreateRequest(requested: 20));

        Assert.IsFalse(result.Success);
        Assert.AreEqual(
            MemoryWriteFailureReason.GuardPage,
            result.FailureReason);
        Assert.AreEqual(0, native.WriteCount);
    }

    [TestMethod]
    public async Task RangeCrossingRegionBoundaryIsRejected()
    {
        var native = new FakeNativeApi
        {
            Region = new NativeMemoryRegion(
                0x1000,
                0x1000,
                2,
                NativeMemoryConstants.MemCommit,
                NativeMemoryConstants.PageReadWrite,
                NativeMemoryConstants.MemPrivate),
        };
        using var writer = CreateWriter(native);

        var result = await writer.WriteAsync(
            CreateRequest(requested: 20));

        Assert.IsFalse(result.Success);
        Assert.AreEqual(
            MemoryWriteFailureReason.InvalidAddress,
            result.FailureReason);
        Assert.AreEqual(0, native.WriteCount);
    }

    [TestMethod]
    public async Task PartialNativeWriteReportsWrittenByteCount()
    {
        var native = new FakeNativeApi
        {
            WriteSuccess = false,
            BytesWritten = 2,
            WriteErrorCode =
                NativeMemoryConstants.ErrorPartialCopy,
        };
        native.Reads.Enqueue(ReadSuccess(BitConverter.GetBytes(10)));
        using var writer = CreateWriter(native);

        var result = await writer.WriteAsync(
            CreateRequest(requested: 20));

        Assert.IsFalse(result.Success);
        Assert.AreEqual(
            MemoryWriteFailureReason.PartialWrite,
            result.FailureReason);
        Assert.AreEqual(2, result.WrittenByteCount);
        Assert.AreEqual(1, native.WriteCount);
    }

    [TestMethod]
    public async Task VerificationMismatchIsNotReportedAsSuccess()
    {
        var native = new FakeNativeApi();
        native.Reads.Enqueue(ReadSuccess(BitConverter.GetBytes(10)));
        native.Reads.Enqueue(ReadSuccess(BitConverter.GetBytes(99)));
        using var writer = CreateWriter(native);

        var result = await writer.WriteAsync(
            CreateRequest(requested: 20));

        Assert.IsFalse(result.Success);
        Assert.AreEqual(
            MemoryWriteFailureReason.VerificationMismatch,
            result.FailureReason);
        Assert.AreEqual(
            MemoryWriteVerificationStatus.Mismatch,
            result.Verification.Status);
        Assert.AreEqual(4, result.WrittenByteCount);
    }

    [TestMethod]
    public async Task ReadBackFailurePreservesCompletedWriteCount()
    {
        var native = new FakeNativeApi();
        native.Reads.Enqueue(ReadSuccess(BitConverter.GetBytes(10)));
        native.Reads.Enqueue(
            new ReadResponse(
                false,
                [],
                0,
                NativeMemoryConstants.ErrorPartialCopy));
        using var writer = CreateWriter(native);

        var result = await writer.WriteAsync(
            CreateRequest(requested: 20));

        Assert.IsFalse(result.Success);
        Assert.AreEqual(
            MemoryWriteFailureReason.VerificationReadFailed,
            result.FailureReason);
        Assert.AreEqual(
            MemoryWriteVerificationStatus.ReadFailed,
            result.Verification.Status);
        Assert.AreEqual(4, result.WrittenByteCount);
    }

    [TestMethod]
    public async Task SessionMismatchIsRejectedBeforeIdentityOrNativeApi()
    {
        var native = new FakeNativeApi();
        var validator = new FakeIdentityValidator(Result.Success());
        var session = CreateSession() with
        {
            SessionId = Guid.NewGuid(),
        };
        using var writer = CreateWriter(
            native,
            validator,
            session);

        var result = await writer.WriteAsync(
            CreateRequest(requested: 20));

        Assert.IsFalse(result.Success);
        Assert.AreEqual(
            MemoryWriteFailureReason.SessionInvalid,
            result.FailureReason);
        Assert.AreEqual(0, validator.CallCount);
        Assert.AreEqual(0, native.OpenCount);
    }

    [TestMethod]
    public async Task ExitedTargetIsRejectedBeforeOpeningWriteHandle()
    {
        var native = new FakeNativeApi();
        var validator = new FakeIdentityValidator(
            Result.Failure(
                new Error(
                    ErrorCode.NotFound,
                    "Target exited.")));
        using var writer = CreateWriter(native, validator);

        var result = await writer.WriteAsync(
            CreateRequest(requested: 20));

        Assert.IsFalse(result.Success);
        Assert.AreEqual(
            MemoryWriteFailureReason.TargetExited,
            result.FailureReason);
        Assert.AreEqual(0, native.OpenCount);
    }

    [TestMethod]
    public async Task CancelledRequestDoesNotOpenTarget()
    {
        var native = new FakeNativeApi();
        using var writer = CreateWriter(native);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await writer.WriteAsync(
            CreateRequest(requested: 20),
            cancellation.Token);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(
            MemoryWriteFailureReason.Cancelled,
            result.FailureReason);
        Assert.AreEqual(0, native.OpenCount);
    }

    [TestMethod]
    public void RegionValidatorRejectsOverflowAndReservedRegions()
    {
        var validator = new MemoryWriteRegionValidator();
        var writable = MemoryRegionMapper.Map(
            new NativeMemoryRegion(
                ulong.MaxValue - 8,
                ulong.MaxValue - 8,
                8,
                NativeMemoryConstants.MemCommit,
                NativeMemoryConstants.PageReadWrite,
                NativeMemoryConstants.MemPrivate));
        var reserved = MemoryRegionMapper.Map(
            new NativeMemoryRegion(
                0x1000,
                0x1000,
                0x1000,
                NativeMemoryConstants.MemReserve,
                NativeMemoryConstants.PageReadWrite,
                NativeMemoryConstants.MemPrivate));

        Assert.AreEqual(
            MemoryWriteFailureReason.RangeOverflow,
            validator.Validate(writable, ulong.MaxValue - 2, 4));
        Assert.AreEqual(
            MemoryWriteFailureReason.RegionNotCommitted,
            validator.Validate(reserved, 0x1000, 4));
    }

    private static WindowsMemoryWriter CreateWriter(
        FakeNativeApi native,
        FakeIdentityValidator? identityValidator = null,
        MonitoringSession? session = null)
    {
        return new WindowsMemoryWriter(
            native,
            identityValidator ??
                new FakeIdentityValidator(Result.Success()),
            new StubSessionService(session ?? CreateSession()),
            new MemoryWriteRegionValidator(),
            new MemoryWriteVerificationService(),
            new FixedTimeProvider(Now));
    }

    private static MemoryWriteRequest CreateRequest(
        int requested,
        int? expected = null)
    {
        return new MemoryWriteRequest(
            SessionId,
            Identity,
            0x1000,
            ScanValueType.Int32,
            requested.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            BitConverter.GetBytes(requested),
            expected.HasValue
                ? BitConverter.GetBytes(expected.Value)
                : [],
            expected.HasValue,
            verifyAfterWrite: true,
            MemoryWriteSource.SavedAddress,
            "Windows writer test",
            Now);
    }

    private static Guid SessionId { get; } = Guid.NewGuid();

    private static MonitoringSession CreateSession()
    {
        return new MonitoringSession
        {
            SessionId = SessionId,
            Identity = Identity,
            State = MonitoringSessionState.Connected,
            CreatedAt = Now,
            ConnectedAt = Now,
        };
    }

    private static NativeMemoryRegion Region(uint protection)
    {
        return new NativeMemoryRegion(
            0x1000,
            0x1000,
            0x1000,
            NativeMemoryConstants.MemCommit,
            protection,
            NativeMemoryConstants.MemPrivate);
    }

    private static ReadResponse ReadSuccess(byte[] data)
    {
        return new ReadResponse(true, data, data.Length, 0);
    }

    private sealed class FakeNativeApi : IMemoryWriterNativeApi
    {
        public NativeMemoryRegion Region { get; init; } =
            WindowsMemoryWriterTests.Region(
                NativeMemoryConstants.PageReadWrite);

        public Queue<ReadResponse> Reads { get; } = [];

        public bool WriteSuccess { get; init; } = true;

        public int? BytesWritten { get; init; }

        public int WriteErrorCode { get; init; }

        public int OpenCount { get; private set; }

        public int QueryCount { get; private set; }

        public int ReadCount { get; private set; }

        public int WriteCount { get; private set; }

        public WindowsProcessWriteHandle? LastHandle { get; private set; }

        public WindowsProcessWriteHandle OpenProcess(int processId)
        {
            OpenCount++;
            LastHandle = new WindowsProcessWriteHandle(
                new SafeProcessHandle(
                    new IntPtr(123),
                    ownsHandle: false));
            return LastHandle;
        }

        public bool TryQuery(
            WindowsProcessWriteHandle processHandle,
            ulong address,
            out NativeMemoryRegion region,
            out int errorCode)
        {
            QueryCount++;
            region = Region;
            errorCode = 0;
            return true;
        }

        public bool TryRead(
            WindowsProcessWriteHandle processHandle,
            ulong address,
            byte[] buffer,
            out int bytesRead,
            out int errorCode)
        {
            ReadCount++;
            var response = Reads.Dequeue();
            Array.Copy(
                response.Data,
                buffer,
                Math.Min(response.Data.Length, buffer.Length));
            bytesRead = response.BytesRead;
            errorCode = response.ErrorCode;
            return response.Success;
        }

        public bool TryWrite(
            WindowsProcessWriteHandle processHandle,
            ulong address,
            byte[] buffer,
            out int bytesWritten,
            out int errorCode)
        {
            WriteCount++;
            bytesWritten = BytesWritten ?? buffer.Length;
            errorCode = WriteErrorCode;
            return WriteSuccess;
        }
    }

    private sealed class FakeIdentityValidator(
        Result result) : IProcessIdentityValidator
    {
        public int CallCount { get; private set; }

        public Result Validate(MonitoringSessionIdentity identity)
        {
            CallCount++;
            return result;
        }
    }

    private sealed class StubSessionService(
        MonitoringSession session) : IMonitoringSessionService
    {
        public MonitoringSession? CurrentSession { get; } = session;

        public event EventHandler<MonitoringSessionChangedEventArgs>?
            SessionChanged
        {
            add { }
            remove { }
        }

        public Task<Result<MonitoringSession>> StartAsync(
            MonitoringSessionIdentity identity,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<Result<MonitoringSession>> CheckLivenessAsync(
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<Result> StopAsync(
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(
        DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed record ReadResponse(
        bool Success,
        byte[] Data,
        int BytesRead,
        int ErrorCode);
}
