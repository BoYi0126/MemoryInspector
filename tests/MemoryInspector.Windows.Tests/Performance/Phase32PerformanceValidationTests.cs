using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MemoryInspector.Application.Memory;
using MemoryInspector.Application.Scanning.Snapshots;
using MemoryInspector.Common;
using MemoryInspector.Core.Monitoring;
using MemoryInspector.Core.Processes;
using MemoryInspector.Core.Scanning;
using MemoryInspector.Windows.Configuration;
using MemoryInspector.Windows.Memory;
using MemoryInspector.Windows.Scanning.Snapshots;
using MemoryInspector.Windows.Tests.Configuration;

namespace MemoryInspector.Windows.Tests.Performance;

[TestClass]
public sealed class Phase32PerformanceValidationTests
{
    private const long MaximumWorkingSetGrowthBytes =
        256L * 1024 * 1024;

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [TestCategory("Performance")]
    [Timeout(60_000)]
    public async Task SnapshotWriteAndReadMeetBaselineWithoutStreamLeak()
    {
        const int recordCount = 250_000;
        const int pageSize = 5_000;
        using var temporaryDirectory = new TemporaryDirectory();
        var paths = new AppPathService(
            temporaryDirectory.RootPath);
        using var storage = new BinarySnapshotStorage(
            paths,
            TimeProvider.System);
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        var baselineWorkingSet = process.WorkingSet64;
        await using var sampler = new WorkingSetSampler(process);
        var writeTimer = Stopwatch.StartNew();

        var write = await storage.WriteAsync(
            new SnapshotWriteRequest(
                Guid.NewGuid(),
                nodeId: 1,
                ScanValueType.Int32,
                includeValues: true,
                expectedRecordCount: recordCount),
            Records(recordCount));

        writeTimer.Stop();
        Assert.IsTrue(
            write.IsSuccess,
            write.IsFailure
                ? write.Error.ToDisplayMessage()
                : null);
        var readTimer = Stopwatch.StartNew();
        long recordsRead = 0;
        var totalPages =
            (recordCount + pageSize - 1) / pageSize;

        for (var page = 1; page <= totalPages; page++)
        {
            var result = await storage.ReadPageAsync(
                write.Value,
                page,
                pageSize);
            Assert.IsTrue(result.IsSuccess);
            recordsRead += result.Value.Items.Count;
        }

        readTimer.Stop();
        var peakWorkingSet = await sampler.StopAsync();
        var workingSetGrowth = Math.Max(
            0,
            peakWorkingSet - baselineWorkingSet);
        var writeThroughput =
            recordCount / writeTimer.Elapsed.TotalSeconds;
        var readThroughput =
            recordsRead / readTimer.Elapsed.TotalSeconds;

        await using (var exclusive = new FileStream(
            write.Value.FilePath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None))
        {
            Assert.IsTrue(exclusive.CanWrite);
        }

        TestContext.WriteLine(
            $"METRIC snapshot_write_records_per_second=" +
            $"{writeThroughput:F0}");
        Console.WriteLine(
            $"METRIC snapshot_write_records_per_second=" +
            $"{writeThroughput:F0}");
        TestContext.WriteLine(
            $"METRIC snapshot_read_records_per_second=" +
            $"{readThroughput:F0}");
        Console.WriteLine(
            $"METRIC snapshot_read_records_per_second=" +
            $"{readThroughput:F0}");
        TestContext.WriteLine(
            $"METRIC snapshot_peak_working_set_growth_bytes=" +
            $"{workingSetGrowth}");
        Console.WriteLine(
            $"METRIC snapshot_peak_working_set_growth_bytes=" +
            $"{workingSetGrowth}");

        Assert.AreEqual(recordCount, recordsRead);
        Assert.IsTrue(
            writeThroughput >= 5_000,
            $"Snapshot write throughput was {writeThroughput:F0} records/s.");
        Assert.IsTrue(
            readThroughput >= 10_000,
            $"Snapshot read throughput was {readThroughput:F0} records/s.");
        Assert.IsTrue(
            workingSetGrowth <= MaximumWorkingSetGrowthBytes,
            $"Working-set growth was {workingSetGrowth:N0} bytes.");
    }

    [TestMethod]
    [TestCategory("Performance")]
    [Timeout(30_000)]
    public async Task RepeatedLiveReadsDoNotLeakProcessHandles()
    {
        const int readCount = 500;
        var memory = Marshal.AllocHGlobal(4_096);

        try
        {
            Marshal.WriteInt32(memory, 123_456_789);
            using var process = Process.GetCurrentProcess();
            var identity = new MonitoringSessionIdentity(
                process.Id,
                new DateTimeOffset(process.StartTime),
                ProcessArchitecture.X64,
                process.ProcessName);
            var provider = new WindowsMemoryReaderProvider(
                new RecordingLogger());
            var request = new MemoryReadRequest(
                (ulong)(nuint)memory,
                4_096);
            var options = new MemoryReadOptions(4_096);
            var warmup = await provider.ReadAsync(
                identity,
                request,
                options);
            Assert.IsTrue(warmup.IsSuccess);
            ForceCollection();
            process.Refresh();
            var handlesBefore = process.HandleCount;
            var timer = Stopwatch.StartNew();

            for (var index = 0; index < readCount; index++)
            {
                var result = await provider.ReadAsync(
                    identity,
                    request,
                    options);
                Assert.IsTrue(result.IsSuccess);
                Assert.IsTrue(result.Value.IsComplete);
            }

            timer.Stop();
            ForceCollection();
            process.Refresh();
            var handleGrowth = process.HandleCount - handlesBefore;
            var operationsPerSecond =
                readCount / timer.Elapsed.TotalSeconds;
            TestContext.WriteLine(
                $"METRIC live_read_operations_per_second=" +
                $"{operationsPerSecond:F0}");
            Console.WriteLine(
                $"METRIC live_read_operations_per_second=" +
                $"{operationsPerSecond:F0}");
            TestContext.WriteLine(
                $"METRIC live_read_handle_growth={handleGrowth}");
            Console.WriteLine(
                $"METRIC live_read_handle_growth={handleGrowth}");

            Assert.IsTrue(
                handleGrowth <= 4,
                $"Process handle count grew by {handleGrowth}.");
            Assert.IsTrue(
                operationsPerSecond >= 25,
                $"Live read rate was {operationsPerSecond:F0}/s.");
        }
        finally
        {
            Marshal.FreeHGlobal(memory);
        }
    }

    private static async IAsyncEnumerable<SnapshotRecord> Records(
        int count,
        [EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        var value = new byte[sizeof(int)];

        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BinaryPrimitives.WriteInt32LittleEndian(value, index);
            yield return new SnapshotRecord(
                new CandidateAddress(
                    0x10_000UL + (ulong)index),
                value);

            if (index > 0 && index % 16_384 == 0)
            {
                await Task.Yield();
            }
        }
    }

    private static void ForceCollection()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private sealed class WorkingSetSampler :
        IAsyncDisposable
    {
        private readonly Process _process;
        private readonly CancellationTokenSource _cancellation = new();
        private readonly Task _samplingTask;
        private long _peakBytes;

        public WorkingSetSampler(Process process)
        {
            _process = process;
            _process.Refresh();
            _peakBytes = _process.WorkingSet64;
            _samplingTask = SampleAsync();
        }

        public async Task<long> StopAsync()
        {
            _cancellation.Cancel();

            try
            {
                await _samplingTask;
            }
            catch (OperationCanceledException)
            {
            }

            return Interlocked.Read(ref _peakBytes);
        }

        public async ValueTask DisposeAsync()
        {
            _ = await StopAsync();
            _cancellation.Dispose();
        }

        private async Task SampleAsync()
        {
            while (!_cancellation.IsCancellationRequested)
            {
                _process.Refresh();
                var current = _process.WorkingSet64;
                var observed = Interlocked.Read(ref _peakBytes);

                while (current > observed)
                {
                    var previous = Interlocked.CompareExchange(
                        ref _peakBytes,
                        current,
                        observed);

                    if (previous == observed)
                    {
                        break;
                    }

                    observed = previous;
                }

                await Task.Delay(
                    TimeSpan.FromMilliseconds(10),
                    _cancellation.Token);
            }
        }
    }
}
