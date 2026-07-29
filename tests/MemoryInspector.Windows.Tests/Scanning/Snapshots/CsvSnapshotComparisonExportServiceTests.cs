using MemoryInspector.Application.Scanning.Snapshots;
using MemoryInspector.Application.Scanning.Snapshots.Comparison;
using MemoryInspector.Common;
using MemoryInspector.Core.Scanning;
using MemoryInspector.Windows.Scanning.Snapshots;

namespace MemoryInspector.Windows.Tests.Scanning.Snapshots;

[TestClass]
public sealed class CsvSnapshotComparisonExportServiceTests
{
    [TestMethod]
    public async Task ExportWritesDifferencesAndSummaryAtomically()
    {
        var directory = CreateDirectory();

        try
        {
            var path = Path.Combine(directory, "comparison.csv");
            var left = Snapshot(1, 1, 12);
            var right = Snapshot(2, 2, 24);
            var summary = new SnapshotComparisonSummary(
                left,
                right,
                addedCount: 1,
                removedCount: 0,
                changedCount: 1,
                unchangedCount: 0);
            var compare = new StubCompareService(
                summary,
                [
                    new SnapshotDifference(
                        0x1000,
                        SnapshotDifferenceKind.Changed,
                        new byte[] { 1, 0, 0, 0 },
                        new byte[] { 2, 0, 0, 0 }),
                    new SnapshotDifference(
                        0x2000,
                        SnapshotDifferenceKind.Added,
                        null,
                        new byte[] { 3, 0, 0, 0 }),
                ]);
            var exporter =
                new CsvSnapshotComparisonExportService(compare);

            var result = await exporter.ExportCsvAsync(
                path,
                left,
                right);

            Assert.IsTrue(result.IsSuccess);
            var content = await File.ReadAllTextAsync(path);
            StringAssert.Contains(
                content,
                "Difference,0x0000000000001000,Changed");
            StringAssert.Contains(
                content,
                "Summary,,Added");
            StringAssert.Contains(
                content,
                "Summary,,StorageBytes");
            Assert.AreEqual(
                0,
                Directory.GetFiles(directory, "*.tmp").Length);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task FailedComparisonPreservesExistingExport()
    {
        var directory = CreateDirectory();

        try
        {
            var path = Path.Combine(directory, "comparison.csv");
            await File.WriteAllTextAsync(path, "existing");
            var left = Snapshot(1, 1, 12);
            var right = Snapshot(2, 1, 12);
            var compare = new StubCompareService(
                new Error(
                    ErrorCode.Serialization,
                    "Snapshot corrupt."));
            var exporter =
                new CsvSnapshotComparisonExportService(compare);

            var result = await exporter.ExportCsvAsync(
                path,
                left,
                right);

            Assert.IsTrue(result.IsFailure);
            Assert.AreEqual(
                "existing",
                await File.ReadAllTextAsync(path));
            Assert.AreEqual(
                0,
                Directory.GetFiles(directory, "*.tmp").Length);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static SnapshotDescriptor Snapshot(
        int nodeId,
        long count,
        long payloadLength)
    {
        return new SnapshotDescriptor(
            Guid.Parse(
                "11111111-2222-3333-4444-555555555555"),
            nodeId,
            SnapshotFormatInfo.CurrentVersion,
            ScanValueType.Int32,
            includesValues: true,
            valueSize: sizeof(int),
            recordSize: 12,
            recordCount: count,
            payloadLength,
            checksum: $"NODE-{nodeId}",
            createdAt: DateTimeOffset.UtcNow,
            filePath: $"node-{nodeId}.snap");
    }

    private static string CreateDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"MemoryInspector-SnapshotCompare-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class StubCompareService :
        ISnapshotCompareService
    {
        private readonly SnapshotComparisonSummary? _summary;
        private readonly IReadOnlyList<SnapshotDifference> _items;
        private readonly Error? _error;

        public StubCompareService(
            SnapshotComparisonSummary summary,
            IReadOnlyList<SnapshotDifference> items)
        {
            _summary = summary;
            _items = items;
        }

        public StubCompareService(Error error)
        {
            _error = error;
            _items = [];
        }

        public Task<Result<SnapshotComparisonPage>> CompareAsync(
            SnapshotDescriptor left,
            SnapshotDescriptor right,
            long pageNumber = 1,
            int pageSize =
                SnapshotCompareService.DefaultDifferencePageSize,
            IProgress<OperationProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public async Task<Result<SnapshotComparisonSummary>> VisitAsync(
            SnapshotDescriptor left,
            SnapshotDescriptor right,
            Func<
                SnapshotDifference,
                CancellationToken,
                ValueTask> visitor,
            IProgress<OperationProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (_error is not null)
            {
                return Result<SnapshotComparisonSummary>.Failure(
                    _error);
            }

            foreach (var item in _items)
            {
                await visitor(item, cancellationToken);
            }

            return Result<SnapshotComparisonSummary>.Success(
                _summary!);
        }
    }
}
