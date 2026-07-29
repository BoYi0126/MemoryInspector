using MemoryInspector.Application.Memory;
using MemoryInspector.Application.Monitoring;
using MemoryInspector.Common;
using MemoryInspector.Core.Monitoring;
using MemoryInspector.Core.Processes;

namespace MemoryInspector.IntegrationTests.Memory;

[TestClass]
public sealed class MemoryReaderServiceTests
{
    private static readonly MonitoringSessionIdentity Identity = new(
        42,
        new DateTimeOffset(2026, 7, 29, 8, 30, 0, TimeSpan.Zero),
        ProcessArchitecture.X64,
        "Target");

    [TestMethod]
    public void RequestAndOptionsValidateBounds()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new MemoryReadRequest(0x1000, 0));
        Assert.ThrowsExactly<OverflowException>(() =>
            new MemoryReadRequest(ulong.MaxValue, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new MemoryReadOptions(0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new MemoryReadOptions(
                MemoryReadOptions.MaximumChunkSizeBytes + 1));
    }

    [TestMethod]
    public void ReadResultCopiesDataAndReportsPartialState()
    {
        var source = new byte[] { 1, 2 };
        var request = new MemoryReadRequest(0x1000, 4);
        var warning = new Error(
            ErrorCode.NotFound,
            "Read stopped early.");
        var result = new MemoryReadResult(
            request,
            source,
            [warning]);

        source[0] = 9;

        Assert.AreEqual((byte)1, result.Data.Span[0]);
        Assert.AreEqual(2, result.BytesRead);
        Assert.IsTrue(result.IsPartial);
        Assert.IsFalse(result.IsComplete);
    }

    [TestMethod]
    public async Task DisconnectedSessionRejectsReadBeforeProviderCall()
    {
        var sessionService = new StubSessionService
        {
            CurrentSession = CreateSession(
                MonitoringSessionState.Disconnected),
        };
        var provider = new DelegateMemoryReaderProvider();
        var service = new MemoryReaderService(
            sessionService,
            provider);

        var result = await service.ReadAsync(0x1000, 4);

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(ErrorCode.InvalidState, result.Error.Code);
        Assert.AreEqual(0, provider.ReadCallCount);
    }

    [TestMethod]
    public async Task BlockReadUsesCurrentSessionIdentityAndOptions()
    {
        var sessionService = new StubSessionService
        {
            CurrentSession = CreateSession(
                MonitoringSessionState.Connected),
        };
        MonitoringSessionIdentity? receivedIdentity = null;
        MemoryReadOptions? receivedOptions = null;
        var provider = new DelegateMemoryReaderProvider
        {
            Read = (identity, request, options) =>
            {
                receivedIdentity = identity;
                receivedOptions = options;
                return Result<MemoryReadResult>.Success(
                    new MemoryReadResult(
                        request,
                        new byte[] { 1, 2, 3, 4 }));
            },
        };
        var service = new MemoryReaderService(
            sessionService,
            provider);

        var result = await service.ReadAsync(
            0x1000,
            4,
            new MemoryReadOptions(2));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(Identity, receivedIdentity);
        Assert.AreEqual(2, receivedOptions!.ChunkSizeBytes);
        CollectionAssert.AreEqual(
            new byte[] { 1, 2, 3, 4 },
            result.Value.Data.ToArray());
    }

    [TestMethod]
    public async Task TypedReadReturnsUnmanagedValue()
    {
        const int expected = 0x12345678;
        var sessionService = new StubSessionService
        {
            CurrentSession = CreateSession(
                MonitoringSessionState.Connected),
        };
        var provider = new DelegateMemoryReaderProvider
        {
            Read = (_, request, _) =>
                Result<MemoryReadResult>.Success(
                    new MemoryReadResult(
                        request,
                        BitConverter.GetBytes(expected))),
        };
        var service = new MemoryReaderService(
            sessionService,
            provider);

        var result = await service.TryReadAsync<int>(0x1000);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(expected, result.Value);
    }

    [TestMethod]
    public async Task TypedReadRejectsPartialValue()
    {
        var sessionService = new StubSessionService
        {
            CurrentSession = CreateSession(
                MonitoringSessionState.Connected),
        };
        var provider = new DelegateMemoryReaderProvider
        {
            Read = (_, request, _) =>
                Result<MemoryReadResult>.Success(
                    new MemoryReadResult(
                        request,
                        new byte[] { 1, 2 },
                        [
                            new Error(
                                ErrorCode.NotFound,
                                "Read stopped early."),
                        ])),
        };
        var service = new MemoryReaderService(
            sessionService,
            provider);

        var result = await service.TryReadAsync<int>(0x1000);

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(ErrorCode.NativeApi, result.Error.Code);
        Assert.IsNotNull(result.Error.Cause);
    }

    [TestMethod]
    public async Task BatchReadUsesOneProviderBatchOperation()
    {
        var requests = new[]
        {
            new MemoryReadRequest(0x1000, 2),
            new MemoryReadRequest(0x2000, 2),
        };
        var sessionService = new StubSessionService
        {
            CurrentSession = CreateSession(
                MonitoringSessionState.Connected),
        };
        var provider = new DelegateMemoryReaderProvider
        {
            Batch = (_, batch, _) =>
                Result<MemoryBatchReadResult>.Success(
                    new MemoryBatchReadResult(
                        batch.Select(request =>
                            new MemoryBatchReadItem(
                                request,
                                Result<MemoryReadResult>.Success(
                                    new MemoryReadResult(
                                        request,
                                        new byte[] { 1, 2 })))))),
        };
        var service = new MemoryReaderService(
            sessionService,
            provider);

        var result = await service.ReadBatchAsync(requests);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1, provider.BatchCallCount);
        Assert.AreEqual(2, result.Value.SucceededCount);
        Assert.AreEqual(0, result.Value.FailedCount);
    }

    [TestMethod]
    public async Task SessionChangeDuringReadInvalidatesResult()
    {
        var original = CreateSession(MonitoringSessionState.Connected);
        var sessionService = new StubSessionService
        {
            CurrentSession = original,
        };
        var provider = new DelegateMemoryReaderProvider
        {
            Read = (_, request, _) =>
            {
                sessionService.CurrentSession = original with
                {
                    SessionId = Guid.NewGuid(),
                };
                return Result<MemoryReadResult>.Success(
                    new MemoryReadResult(
                        request,
                        new byte[] { 1, 2, 3, 4 }));
            },
        };
        var service = new MemoryReaderService(
            sessionService,
            provider);

        var result = await service.ReadAsync(0x1000, 4);

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(ErrorCode.InvalidState, result.Error.Code);
    }

    [TestMethod]
    public async Task InvalidRangeReturnsValidationResult()
    {
        var sessionService = new StubSessionService
        {
            CurrentSession = CreateSession(
                MonitoringSessionState.Connected),
        };
        var service = new MemoryReaderService(
            sessionService,
            new DelegateMemoryReaderProvider());

        var result = await service.ReadAsync(ulong.MaxValue, 2);

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(ErrorCode.Validation, result.Error.Code);
    }

    private static MonitoringSession CreateSession(
        MonitoringSessionState state)
    {
        return new MonitoringSession
        {
            SessionId = Guid.NewGuid(),
            Identity = Identity,
            State = state,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    private sealed class DelegateMemoryReaderProvider
        : IMemoryReaderProvider
    {
        public Func<
            MonitoringSessionIdentity,
            MemoryReadRequest,
            MemoryReadOptions,
            Result<MemoryReadResult>>? Read { get; init; }

        public Func<
            MonitoringSessionIdentity,
            IReadOnlyList<MemoryReadRequest>,
            MemoryReadOptions,
            Result<MemoryBatchReadResult>>? Batch { get; init; }

        public int ReadCallCount { get; private set; }

        public int BatchCallCount { get; private set; }

        public Task<Result<MemoryReadResult>> ReadAsync(
            MonitoringSessionIdentity identity,
            MemoryReadRequest request,
            MemoryReadOptions options,
            CancellationToken cancellationToken = default)
        {
            ReadCallCount++;
            return Task.FromResult(
                Read?.Invoke(identity, request, options) ??
                Result<MemoryReadResult>.Failure(
                    new Error(
                        ErrorCode.Unexpected,
                        "No read response configured.")));
        }

        public Task<Result<MemoryBatchReadResult>> ReadBatchAsync(
            MonitoringSessionIdentity identity,
            IReadOnlyList<MemoryReadRequest> requests,
            MemoryReadOptions options,
            CancellationToken cancellationToken = default)
        {
            BatchCallCount++;
            return Task.FromResult(
                Batch?.Invoke(identity, requests, options) ??
                Result<MemoryBatchReadResult>.Failure(
                    new Error(
                        ErrorCode.Unexpected,
                        "No batch response configured.")));
        }
    }

    private sealed class StubSessionService : IMonitoringSessionService
    {
        public MonitoringSession? CurrentSession { get; set; }

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
}
