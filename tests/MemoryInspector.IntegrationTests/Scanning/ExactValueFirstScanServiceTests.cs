using MemoryInspector.Application.Memory;
using MemoryInspector.Application.Scanning;
using MemoryInspector.Common;
using MemoryInspector.Core.Memory;
using MemoryInspector.Core.Scanning;

namespace MemoryInspector.IntegrationTests.Scanning;

[TestClass]
public sealed class ExactValueFirstScanServiceTests
{
    private const ulong BaseAddress = 0x1_000;
    private readonly IScanValueParser _parser =
        new InvariantScanValueParser();

    [TestMethod]
    public async Task FindsInt32ValuesInReadableMemory()
    {
        var memory = new byte[24];
        BitConverter.GetBytes(42).CopyTo(memory, 4);
        BitConverter.GetBytes(42).CopyTo(memory, 16);
        var fixture = CreateFixture(
            [ReadableRegion(BaseAddress, (ulong)memory.Length)],
            BufferReader(BaseAddress, memory));

        var result = await fixture.Service.ScanExactValueAsync(
            ExactRequest(
                ScanValueType.Int32,
                "42",
                ScanAlignmentMode.Aligned),
            new FirstScanOptions(8));

        Assert.IsTrue(result.IsSuccess);
        CollectionAssert.AreEqual(
            new[]
            {
                new CandidateAddress(BaseAddress + 4),
                new CandidateAddress(BaseAddress + 16),
            },
            result.Value.Candidates.ToArray());
        Assert.AreEqual(24L, result.Value.Summary.ScannedBytes);
        Assert.IsFalse(result.Value.Summary.IsPartial);
    }

    [TestMethod]
    public async Task ChunkOverlapFindsAValueAcrossTheBoundary()
    {
        var memory = Enumerable
            .Repeat((byte)0xCC, 12)
            .ToArray();
        BitConverter.GetBytes(0x12345678).CopyTo(memory, 4);
        var reader = BufferReader(BaseAddress, memory);
        var fixture = CreateFixture(
            [ReadableRegion(BaseAddress, (ulong)memory.Length)],
            reader);

        var result = await fixture.Service.ScanExactValueAsync(
            ExactRequest(
                ScanValueType.Int32,
                "305419896",
                ScanAlignmentMode.Unaligned),
            new FirstScanOptions(6));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1, result.Value.Candidates.Length);
        Assert.AreEqual(
            BaseAddress + 4,
            result.Value.Candidates.Span[0].Address);
        CollectionAssert.AreEqual(
            new[] { BaseAddress, BaseAddress + 3, BaseAddress + 6 },
            reader.Requests.Select(item => item.Address).ToArray());
    }

    [TestMethod]
    public async Task DuplicateAddressesFromOverlappingRegionsAreRemoved()
    {
        var memory = BitConverter.GetBytes(42);
        var region = ReadableRegion(
            BaseAddress,
            (ulong)memory.Length);
        var fixture = CreateFixture(
            [region, region],
            BufferReader(BaseAddress, memory));

        var result = await fixture.Service.ScanExactValueAsync(
            ExactRequest(
                ScanValueType.Int32,
                "42",
                ScanAlignmentMode.Aligned),
            new FirstScanOptions(4));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1, result.Value.Candidates.Length);
        Assert.AreEqual(
            BaseAddress,
            result.Value.Candidates.Span[0].Address);
    }

    [TestMethod]
    public async Task AlignmentModeControlsCandidateAddresses()
    {
        var memory = BitConverter.GetBytes(42);
        var baseAddress = BaseAddress + 1;
        var region = ReadableRegion(
            baseAddress,
            (ulong)memory.Length);
        var reader = BufferReader(baseAddress, memory);
        var fixture = CreateFixture([region], reader);

        var aligned = await fixture.Service.ScanExactValueAsync(
            ExactRequest(
                ScanValueType.Int32,
                "42",
                ScanAlignmentMode.Aligned),
            new FirstScanOptions(4));
        var unaligned = await fixture.Service.ScanExactValueAsync(
            ExactRequest(
                ScanValueType.Int32,
                "42",
                ScanAlignmentMode.Unaligned),
            new FirstScanOptions(4));

        Assert.AreEqual(0, aligned.Value.Candidates.Length);
        Assert.AreEqual(1, unaligned.Value.Candidates.Length);
        Assert.AreEqual(
            baseAddress,
            unaligned.Value.Candidates.Span[0].Address);
    }

    [TestMethod]
    public async Task RegionPolicySkipsUnreadableAndTooSmallRegions()
    {
        var readable = ReadableRegion(BaseAddress, 8);
        var unreadable = new MemoryRegion(
            BaseAddress + 0x100,
            8,
            BaseAddress + 0x100,
            MemoryRegionState.Committed,
            MemoryRegionType.Private,
            MemoryProtection.NoAccess);
        var tooSmall = ReadableRegion(
            BaseAddress + 0x200,
            2);
        var reader = BufferReader(
            BaseAddress,
            BitConverter.GetBytes(42)
                .Concat(new byte[4])
                .ToArray());
        var fixture = CreateFixture(
            [unreadable, tooSmall, readable],
            reader);

        var result = await fixture.Service.ScanExactValueAsync(
            ExactRequest(
                ScanValueType.Int32,
                "42",
                ScanAlignmentMode.Aligned),
            new FirstScanOptions(8));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1, result.Value.ScannedRegionCount);
        Assert.AreEqual(2, result.Value.SkippedRegionCount);
        Assert.AreEqual(1, reader.Requests.Count);
    }

    [TestMethod]
    public async Task PartialReadKeepsAvailableMatchesAndWarnings()
    {
        var requestWarning = new Error(
            ErrorCode.NotFound,
            "The rest of the chunk is unavailable.");
        var reader = new DelegateMemoryReaderService
        {
            Read = (address, length, _) =>
                Task.FromResult(
                    Result<MemoryReadResult>.Success(
                        new MemoryReadResult(
                            new MemoryReadRequest(address, length),
                            BitConverter.GetBytes(42),
                            [requestWarning]))),
        };
        var fixture = CreateFixture(
            [ReadableRegion(BaseAddress, 8)],
            reader);

        var result = await fixture.Service.ScanExactValueAsync(
            ExactRequest(
                ScanValueType.Int32,
                "42",
                ScanAlignmentMode.Aligned),
            new FirstScanOptions(8));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1, result.Value.Candidates.Length);
        Assert.AreEqual(1, result.Value.Warnings.Count);
        Assert.IsTrue(result.Value.Summary.IsPartial);
        Assert.AreEqual(4L, result.Value.Summary.ScannedBytes);
    }

    [TestMethod]
    public async Task RecoverableReadFailureSkipsChunkAndContinues()
    {
        var firstAddress = BaseAddress;
        var secondAddress = BaseAddress + 0x100;
        var reader = new DelegateMemoryReaderService
        {
            Read = (address, length, _) =>
            {
                if (address == firstAddress)
                {
                    return Task.FromResult(
                        Result<MemoryReadResult>.Failure(
                            new Error(
                                ErrorCode.NotFound,
                                "Chunk is unavailable.")));
                }

                return Task.FromResult(
                    Result<MemoryReadResult>.Success(
                        new MemoryReadResult(
                            new MemoryReadRequest(address, length),
                            BitConverter.GetBytes(42))));
            },
        };
        var fixture = CreateFixture(
            [
                ReadableRegion(firstAddress, 4),
                ReadableRegion(secondAddress, 4),
            ],
            reader);

        var result = await fixture.Service.ScanExactValueAsync(
            ExactRequest(
                ScanValueType.Int32,
                "42",
                ScanAlignmentMode.Aligned),
            new FirstScanOptions(4));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1, result.Value.Candidates.Length);
        Assert.AreEqual(
            secondAddress,
            result.Value.Candidates.Span[0].Address);
        Assert.AreEqual(1, result.Value.Warnings.Count);
        Assert.IsTrue(result.Value.Summary.IsPartial);
    }

    [TestMethod]
    public async Task ResultLimitStopsTheScanWithoutExceedingTheLimit()
    {
        var memory = new byte[20];
        var fixture = CreateFixture(
            [ReadableRegion(BaseAddress, (ulong)memory.Length)],
            BufferReader(BaseAddress, memory));

        var result = await fixture.Service.ScanExactValueAsync(
            ExactRequest(
                ScanValueType.Byte,
                "0",
                ScanAlignmentMode.Unaligned,
                maximumResults: 3),
            new FirstScanOptions(8));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(3, result.Value.Candidates.Length);
        Assert.IsTrue(result.Value.IsResultLimitReached);
        Assert.IsTrue(result.Value.Summary.IsPartial);
        Assert.AreEqual(
            ErrorCode.ResourceExhausted,
            result.Value.Warnings[^1].Code);
    }

    [TestMethod]
    public async Task CancellationReturnsCancelledResult()
    {
        using var cancellation = new CancellationTokenSource();
        var reader = new DelegateMemoryReaderService
        {
            Read = (_, _, token) =>
            {
                cancellation.Cancel();
                return Task.FromCanceled<Result<MemoryReadResult>>(token);
            },
        };
        var fixture = CreateFixture(
            [ReadableRegion(BaseAddress, 8)],
            reader);

        var result = await fixture.Service.ScanExactValueAsync(
            ExactRequest(
                ScanValueType.Int32,
                "42",
                ScanAlignmentMode.Aligned),
            new FirstScanOptions(8),
            cancellationToken: cancellation.Token);

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(ErrorCode.Cancelled, result.Error.Code);
    }

    [TestMethod]
    public async Task ProgressReportsChunkLevelCompletion()
    {
        var memory = new byte[12];
        var progress = new CapturingProgress();
        var fixture = CreateFixture(
            [ReadableRegion(BaseAddress, (ulong)memory.Length)],
            BufferReader(BaseAddress, memory));

        var result = await fixture.Service.ScanExactValueAsync(
            ExactRequest(
                ScanValueType.Int32,
                "42",
                ScanAlignmentMode.Unaligned),
            new FirstScanOptions(6),
            progress);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(4, progress.Reports.Count);
        Assert.AreEqual(0L, progress.Reports[0].Completed);
        Assert.AreEqual(12L, progress.Reports[^1].Completed);
        Assert.AreEqual(12L, progress.Reports[^1].Total);
    }

    [TestMethod]
    public async Task InvalidFirstScanRequestDoesNotQueryRegions()
    {
        var regions = new DelegateMemoryRegionService(
            Result<MemoryRegionQueryResult>.Success(
                new MemoryRegionQueryResult([])));
        var fixture = CreateFixture(
            regions,
            new DelegateMemoryReaderService());
        var unknownRequest = ScanRequest.Create(
            ScanValueType.Int32,
            ScanComparisonMode.UnknownInitialValue,
            searchValue: null,
            ScanAlignmentMode.Aligned).Value;

        var invalidMode = await fixture.Service.ScanExactValueAsync(
            unknownRequest);
        var invalidChunk = await fixture.Service.ScanExactValueAsync(
            ExactRequest(
                ScanValueType.Int32,
                "42",
                ScanAlignmentMode.Aligned),
            new FirstScanOptions(2));

        Assert.AreEqual(ErrorCode.Validation, invalidMode.Error.Code);
        Assert.AreEqual(ErrorCode.Validation, invalidChunk.Error.Code);
        Assert.AreEqual(0, regions.CallCount);
    }

    [TestMethod]
    public async Task ScanWorkDoesNotBlockTheCallingThread()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var regions = new DelegateMemoryRegionService(
            Result<MemoryRegionQueryResult>.Success(
                new MemoryRegionQueryResult([])))
        {
            OnCall = () =>
            {
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(5));
            },
        };
        var fixture = CreateFixture(
            regions,
            new DelegateMemoryReaderService());

        var scanTask = fixture.Service.ScanExactValueAsync(
                ExactRequest(
                    ScanValueType.Byte,
                    "1",
                    ScanAlignmentMode.Aligned));

        Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(5)));
        Assert.IsFalse(scanTask.IsCompleted);

        release.Set();
        var result = await scanTask;

        Assert.IsTrue(result.IsSuccess);
    }

    private ScanRequest ExactRequest(
        ScanValueType valueType,
        string input,
        ScanAlignmentMode alignment,
        int maximumResults = ScanRequest.DefaultMaximumResults)
    {
        var value = _parser.Parse(input, valueType).Value;
        return ScanRequest.Create(
            valueType,
            ScanComparisonMode.ExactValue,
            value,
            alignment,
            maximumResults: maximumResults).Value;
    }

    private static TestFixture CreateFixture(
        IReadOnlyList<MemoryRegion> regions,
        IMemoryReaderService reader)
    {
        return CreateFixture(
            new DelegateMemoryRegionService(
                Result<MemoryRegionQueryResult>.Success(
                    new MemoryRegionQueryResult(regions))),
            reader);
    }

    private static TestFixture CreateFixture(
        IMemoryRegionService regions,
        IMemoryReaderService reader)
    {
        return new TestFixture(
            new ExactValueFirstScanService(
                regions,
                reader,
                new DefaultValueMatcher(),
                TimeProvider.System));
    }

    private static DelegateMemoryReaderService BufferReader(
        ulong baseAddress,
        byte[] memory)
    {
        return new DelegateMemoryReaderService
        {
            Read = (address, length, _) =>
            {
                var offset = checked((int)(address - baseAddress));
                var available = Math.Min(
                    length,
                    memory.Length - offset);
                var data = memory
                    .AsSpan(offset, available)
                    .ToArray();
                return Task.FromResult(
                    Result<MemoryReadResult>.Success(
                        new MemoryReadResult(
                            new MemoryReadRequest(address, length),
                            data)));
            },
        };
    }

    private static MemoryRegion ReadableRegion(
        ulong address,
        ulong size)
    {
        return new MemoryRegion(
            address,
            size,
            address,
            MemoryRegionState.Committed,
            MemoryRegionType.Private,
            MemoryProtection.ReadWrite);
    }

    private sealed record TestFixture(
        IFirstScanService Service);

    private sealed class DelegateMemoryRegionService
        : IMemoryRegionService
    {
        private readonly Result<MemoryRegionQueryResult> _result;

        public DelegateMemoryRegionService(
            Result<MemoryRegionQueryResult> result)
        {
            _result = result;
        }

        public int CallCount { get; private set; }

        public Action? OnCall { get; init; }

        public Task<Result<MemoryRegionQueryResult>> GetRegionsAsync(
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            OnCall?.Invoke();
            return Task.FromResult(_result);
        }
    }

    private sealed class DelegateMemoryReaderService
        : IMemoryReaderService
    {
        public Func<
            ulong,
            int,
            CancellationToken,
            Task<Result<MemoryReadResult>>>? Read { get; init; }

        public List<MemoryReadRequest> Requests { get; } = [];

        public Task<Result<MemoryReadResult>> ReadAsync(
            ulong address,
            int length,
            MemoryReadOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(new MemoryReadRequest(address, length));
            return Read?.Invoke(
                address,
                length,
                cancellationToken) ??
                Task.FromResult(
                    Result<MemoryReadResult>.Failure(
                        new Error(
                            ErrorCode.Unexpected,
                            "No read response configured.")));
        }

        public Task<Result<T>> TryReadAsync<T>(
            ulong address,
            MemoryReadOptions? options = null,
            CancellationToken cancellationToken = default)
            where T : unmanaged
        {
            throw new NotSupportedException();
        }

        public Task<Result<MemoryBatchReadResult>> ReadBatchAsync(
            IEnumerable<MemoryReadRequest> requests,
            MemoryReadOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class CapturingProgress
        : IProgress<OperationProgress>
    {
        public List<OperationProgress> Reports { get; } = [];

        public void Report(OperationProgress value)
        {
            Reports.Add(value);
        }
    }
}
