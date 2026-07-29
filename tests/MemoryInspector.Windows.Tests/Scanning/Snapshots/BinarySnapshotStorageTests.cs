using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using MemoryInspector.Application.Scanning.Snapshots;
using MemoryInspector.Common;
using MemoryInspector.Core.Scanning;
using MemoryInspector.Windows.Configuration;
using MemoryInspector.Windows.Scanning.Snapshots;
using MemoryInspector.Windows.Tests.Configuration;

namespace MemoryInspector.Windows.Tests.Scanning.Snapshots;

[TestClass]
public sealed class BinarySnapshotStorageTests
{
    [TestMethod]
    public async Task WritesOpensAndPagesFixedValueRecords()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        using var storage = CreateStorage(temporaryDirectory);
        var sessionId = Guid.NewGuid();
        var request = new SnapshotWriteRequest(
            sessionId,
            nodeId: 1,
            ScanValueType.Int32,
            includeValues: true,
            expectedRecordCount: 5);

        var writeResult = await storage.WriteAsync(
            request,
            GenerateRecords(
                count: 5,
                includeValues: true));

        Assert.IsTrue(writeResult.IsSuccess);
        Assert.AreEqual(5L, writeResult.Value.RecordCount);
        Assert.AreEqual(12, writeResult.Value.RecordSize);
        Assert.AreEqual(64, writeResult.Value.Checksum.Length);
        Assert.IsTrue(File.Exists(writeResult.Value.FilePath));
        Assert.IsTrue(File.Exists(Path.Combine(
            Path.GetDirectoryName(writeResult.Value.FilePath)!,
            "index.bin")));
        Assert.AreEqual(
            0,
            Directory
                .EnumerateFiles(
                    Path.GetDirectoryName(
                        writeResult.Value.FilePath)!,
                    "*.tmp-*",
                    SearchOption.TopDirectoryOnly)
                .Count());
        Assert.AreEqual(
            BinarySnapshotStorage.HeaderSize + 5L * 12,
            new FileInfo(writeResult.Value.FilePath).Length);

        var openResult = await storage.OpenAsync(sessionId, 1);
        var pageResult = await storage.ReadPageAsync(
            openResult.Value,
            pageNumber: 2,
            pageSize: 2);

        Assert.IsTrue(openResult.IsSuccess);
        Assert.IsTrue(pageResult.IsSuccess);
        Assert.AreEqual(5L, pageResult.Value.TotalCount);
        Assert.AreEqual(2, pageResult.Value.Items.Count);
        Assert.AreEqual(
            0x1_002UL,
            pageResult.Value.Items[0].Candidate.Address);
        Assert.AreEqual(
            2,
            BinaryPrimitives.ReadInt32LittleEndian(
                pageResult.Value.Items[0].Value.Span));
        Assert.AreEqual(
            3,
            BinaryPrimitives.ReadInt32LittleEndian(
                pageResult.Value.Items[1].Value.Span));
    }

    [TestMethod]
    public async Task StreamsOneMillionAddressRecordsAndReadsOnePage()
    {
        const int recordCount = 1_000_000;
        using var temporaryDirectory = new TemporaryDirectory();
        using var storage = CreateStorage(temporaryDirectory);
        var progress = new CapturingProgress();
        var request = new SnapshotWriteRequest(
            Guid.NewGuid(),
            nodeId: 7,
            ScanValueType.UInt64,
            includeValues: false,
            expectedRecordCount: recordCount);

        var writeResult = await storage.WriteAsync(
            request,
            GenerateRecords(
                recordCount,
                includeValues: false),
            progress);
        var pageResult = await storage.ReadPageAsync(
            writeResult.Value,
            pageNumber: 1000,
            pageSize: 1000);

        Assert.IsTrue(writeResult.IsSuccess);
        Assert.AreEqual(recordCount, writeResult.Value.RecordCount);
        Assert.AreEqual(
            BinarySnapshotStorage.HeaderSize +
            recordCount * sizeof(ulong),
            new FileInfo(writeResult.Value.FilePath).Length);
        Assert.IsTrue(pageResult.IsSuccess);
        Assert.AreEqual(1000, pageResult.Value.Items.Count);
        Assert.AreEqual(
            0x1_000UL + 999_000UL,
            pageResult.Value.Items[0].Candidate.Address);
        Assert.AreEqual(
            0x1_000UL + 999_999UL,
            pageResult.Value.Items[^1].Candidate.Address);
        Assert.IsTrue(
            progress.Reports.Count < 300,
            "Progress must be batched rather than reported per record.");
        Assert.AreEqual(
            recordCount,
            progress.Reports[^1].Completed);
    }

    [TestMethod]
    public async Task ChecksumRejectsCorruptedPayload()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        using var storage = CreateStorage(temporaryDirectory);
        var request = new SnapshotWriteRequest(
            Guid.NewGuid(),
            nodeId: 1,
            ScanValueType.Byte,
            includeValues: false,
            expectedRecordCount: 2);
        var writeResult = await storage.WriteAsync(
            request,
            GenerateRecords(2, includeValues: false));

        await using (var stream = new FileStream(
            writeResult.Value.FilePath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None))
        {
            stream.Position = BinarySnapshotStorage.HeaderSize;
            var original = stream.ReadByte();
            stream.Position = BinarySnapshotStorage.HeaderSize;
            stream.WriteByte((byte)(original ^ 0xFF));
            await stream.FlushAsync();
        }

        var openResult = await storage.OpenAsync(
            request.SessionId,
            request.NodeId);

        Assert.IsTrue(openResult.IsFailure);
        Assert.AreEqual(
            ErrorCode.Serialization,
            openResult.Error.Code);
    }

    [TestMethod]
    public async Task UnsupportedVersionIsRejected()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        using var storage = CreateStorage(temporaryDirectory);
        var request = new SnapshotWriteRequest(
            Guid.NewGuid(),
            nodeId: 1,
            ScanValueType.Byte,
            includeValues: false,
            expectedRecordCount: 1);
        var writeResult = await storage.WriteAsync(
            request,
            GenerateRecords(1, includeValues: false));

        await using (var stream = new FileStream(
            writeResult.Value.FilePath,
            FileMode.Open,
            FileAccess.Write,
            FileShare.None))
        {
            stream.Position = 8;
            Span<byte> version = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(version, 999);
            stream.Write(version);
            await stream.FlushAsync();
        }

        var openResult = await storage.OpenAsync(
            request.SessionId,
            request.NodeId);

        Assert.IsTrue(openResult.IsFailure);
        Assert.AreEqual(
            ErrorCode.Serialization,
            openResult.Error.Code);
    }

    [TestMethod]
    public async Task InvalidRecordLayoutDoesNotCommitSnapshot()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        using var storage = CreateStorage(temporaryDirectory);
        var request = new SnapshotWriteRequest(
            Guid.NewGuid(),
            nodeId: 1,
            ScanValueType.Int32,
            includeValues: true,
            expectedRecordCount: 1);
        var invalidRecord = new SnapshotRecord(
            new CandidateAddress(0x1_000),
            new byte[] { 1, 2 });

        var result = await storage.WriteAsync(
            request,
            Yield([invalidRecord]));
        var sessionDirectory = Path.Combine(
            temporaryDirectory.RootPath,
            "ApplicationData",
            "Temp",
            request.SessionId.ToString("D"));

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(ErrorCode.Validation, result.Error.Code);
        Assert.IsFalse(File.Exists(Path.Combine(
            sessionDirectory,
            "node_0001.full.bin")));
        Assert.AreEqual(
            0,
            Directory
                .EnumerateFiles(
                    sessionDirectory,
                    "*.tmp-*",
                    SearchOption.TopDirectoryOnly)
                .Count());
    }

    [TestMethod]
    public async Task ExpectedCountMismatchDoesNotCommitSnapshot()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        using var storage = CreateStorage(temporaryDirectory);
        var request = new SnapshotWriteRequest(
            Guid.NewGuid(),
            nodeId: 1,
            ScanValueType.Byte,
            includeValues: false,
            expectedRecordCount: 2);

        var result = await storage.WriteAsync(
            request,
            GenerateRecords(1, includeValues: false));

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(ErrorCode.Validation, result.Error.Code);
        Assert.IsFalse(File.Exists(SnapshotPath(
            temporaryDirectory,
            request.SessionId,
            request.NodeId)));
    }

    [TestMethod]
    public async Task RecoveryCommitsValidTempAndDiscardsCorruption()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        using var storage = CreateStorage(temporaryDirectory);
        var request = new SnapshotWriteRequest(
            Guid.NewGuid(),
            nodeId: 1,
            ScanValueType.UInt16,
            includeValues: false,
            expectedRecordCount: 3);
        var writeResult = await storage.WriteAsync(
            request,
            GenerateRecords(3, includeValues: false));
        var finalPath = writeResult.Value.FilePath;
        var validTemporaryPath = $"{finalPath}.tmp-crash";
        File.Move(finalPath, validTemporaryPath);
        var invalidTemporaryPath = Path.Combine(
            Path.GetDirectoryName(finalPath)!,
            "node_0002.full.bin.tmp-crash");
        await File.WriteAllBytesAsync(
            invalidTemporaryPath,
            [1, 2, 3, 4]);

        var recoveryResult =
            await storage.RecoverIncompleteAsync(
                request.SessionId);
        var openResult = await storage.OpenAsync(
            request.SessionId,
            request.NodeId);

        Assert.IsTrue(recoveryResult.IsSuccess);
        Assert.AreEqual(
            1,
            recoveryResult.Value.RecoveredFileCount);
        Assert.AreEqual(
            1,
            recoveryResult.Value.DiscardedFileCount);
        Assert.IsTrue(openResult.IsSuccess);
        Assert.IsFalse(File.Exists(validTemporaryPath));
        Assert.IsFalse(File.Exists(invalidTemporaryPath));
    }

    [TestMethod]
    public async Task CancellationRemovesIncompleteTempFile()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        using var storage = CreateStorage(temporaryDirectory);
        using var cancellation = new CancellationTokenSource();
        var request = new SnapshotWriteRequest(
            Guid.NewGuid(),
            nodeId: 1,
            ScanValueType.Byte,
            includeValues: false);

        var result = await storage.WriteAsync(
            request,
            CancelAfterOne(cancellation),
            cancellationToken: cancellation.Token);
        var sessionDirectory = Path.GetDirectoryName(
            SnapshotPath(
                temporaryDirectory,
                request.SessionId,
                request.NodeId))!;

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(ErrorCode.Cancelled, result.Error.Code);
        Assert.AreEqual(
            0,
            Directory
                .EnumerateFiles(
                    sessionDirectory,
                    "*",
                    SearchOption.TopDirectoryOnly)
                .Count());
    }

    [TestMethod]
    public async Task EmptySnapshotSupportsOnlyPageOne()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        using var storage = CreateStorage(temporaryDirectory);
        var request = new SnapshotWriteRequest(
            Guid.NewGuid(),
            nodeId: 1,
            ScanValueType.Byte,
            includeValues: false,
            expectedRecordCount: 0);
        var writeResult = await storage.WriteAsync(
            request,
            GenerateRecords(0, includeValues: false));

        var firstPage = await storage.ReadPageAsync(
            writeResult.Value,
            pageNumber: 1,
            pageSize: 100);
        var secondPage = await storage.ReadPageAsync(
            writeResult.Value,
            pageNumber: 2,
            pageSize: 100);

        Assert.IsTrue(firstPage.IsSuccess);
        Assert.AreEqual(0, firstPage.Value.Items.Count);
        Assert.IsTrue(secondPage.IsFailure);
        Assert.AreEqual(
            ErrorCode.Validation,
            secondPage.Error.Code);
    }

    [TestMethod]
    public async Task MissingSnapshotReturnsNotFound()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        using var storage = CreateStorage(temporaryDirectory);

        var result = await storage.OpenAsync(
            Guid.NewGuid(),
            nodeId: 99);

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(ErrorCode.NotFound, result.Error.Code);
    }

    [TestMethod]
    public async Task DeleteRemovesSnapshotAndRebuildsIndex()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        using var storage = CreateStorage(temporaryDirectory);
        var sessionId = Guid.NewGuid();
        var first = await storage.WriteAsync(
            new SnapshotWriteRequest(
                sessionId,
                nodeId: 1,
                ScanValueType.Int32,
                includeValues: true),
            GenerateRecords(2, includeValues: true));
        var second = await storage.WriteAsync(
            new SnapshotWriteRequest(
                sessionId,
                nodeId: 2,
                ScanValueType.Int32,
                includeValues: true),
            GenerateRecords(1, includeValues: true));

        var deleted = await storage.DeleteAsync(
            sessionId,
            nodeId: 2);
        var deletedOpen = await storage.OpenAsync(
            sessionId,
            nodeId: 2);
        var retainedOpen = await storage.OpenAsync(
            sessionId,
            nodeId: 1);

        Assert.IsTrue(first.IsSuccess);
        Assert.IsTrue(second.IsSuccess);
        Assert.IsTrue(deleted.IsSuccess);
        Assert.IsFalse(File.Exists(second.Value.FilePath));
        Assert.AreEqual(
            ErrorCode.NotFound,
            deletedOpen.Error.Code);
        Assert.IsTrue(retainedOpen.IsSuccess);
        Assert.IsTrue(File.Exists(Path.Combine(
            Path.GetDirectoryName(first.Value.FilePath)!,
            "index.bin")));
    }

    private static BinarySnapshotStorage CreateStorage(
        TemporaryDirectory temporaryDirectory)
    {
        var root = Path.Combine(
            temporaryDirectory.RootPath,
            "ApplicationData");
        return new BinarySnapshotStorage(
            new AppPathService(root),
            TimeProvider.System);
    }

    private static string SnapshotPath(
        TemporaryDirectory temporaryDirectory,
        Guid sessionId,
        int nodeId)
    {
        return Path.Combine(
            temporaryDirectory.RootPath,
            "ApplicationData",
            "Temp",
            sessionId.ToString("D"),
            $"node_{nodeId:D4}.full.bin");
    }

    private static async IAsyncEnumerable<SnapshotRecord>
        GenerateRecords(
            int count,
            bool includeValues,
            [EnumeratorCancellation]
            CancellationToken cancellationToken = default)
    {
        var value = new byte[sizeof(int)];

        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (includeValues)
            {
                BinaryPrimitives.WriteInt32LittleEndian(
                    value,
                    index);
            }

            yield return includeValues
                ? new SnapshotRecord(
                    new CandidateAddress(
                        0x1_000UL + (ulong)index),
                    value.ToArray())
                : new SnapshotRecord(
                    new CandidateAddress(
                        0x1_000UL + (ulong)index));

            if (index > 0 && index % 16_384 == 0)
            {
                await Task.Yield();
            }
        }
    }

    private static async IAsyncEnumerable<SnapshotRecord> Yield(
        IEnumerable<SnapshotRecord> records)
    {
        foreach (var record in records)
        {
            yield return record;
        }

        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<SnapshotRecord>
        CancelAfterOne(
            CancellationTokenSource cancellation,
            [EnumeratorCancellation]
            CancellationToken cancellationToken = default)
    {
        yield return new SnapshotRecord(
            new CandidateAddress(0x1_000));
        cancellation.Cancel();
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
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
