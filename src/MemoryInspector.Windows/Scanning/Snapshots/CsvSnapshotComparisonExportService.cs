using System.Globalization;
using System.Text;
using MemoryInspector.Application.Scanning.Snapshots;
using MemoryInspector.Application.Scanning.Snapshots.Comparison;
using MemoryInspector.Common;

namespace MemoryInspector.Windows.Scanning.Snapshots;

public sealed class CsvSnapshotComparisonExportService(
    ISnapshotCompareService compareService) :
    ISnapshotComparisonExportService
{
    private readonly ISnapshotCompareService _compareService =
        Guard.NotNull(compareService);

    public async Task<Result<SnapshotComparisonSummary>> ExportCsvAsync(
        string path,
        SnapshotDescriptor left,
        SnapshotDescriptor right,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Validation(
                "A comparison export path is required.");
        }

        string fullPath;

        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception exception)
            when (exception is
                ArgumentException or
                NotSupportedException)
        {
            return Result<SnapshotComparisonSummary>.Failure(
                new Error(
                    ErrorCode.Validation,
                    "The comparison export path is invalid.",
                    exception));
        }

        var directory = Path.GetDirectoryName(fullPath);

        if (string.IsNullOrWhiteSpace(directory))
        {
            return Validation(
                "The comparison export directory is invalid.");
        }

        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}." +
            $"{Guid.NewGuid():N}.tmp");

        try
        {
            Directory.CreateDirectory(directory);
            Result<SnapshotComparisonSummary> completedComparison;

            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous |
                FileOptions.SequentialScan))
            await using (var writer = new StreamWriter(
                stream,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: true)))
            {
                await writer.WriteLineAsync(
                    "RecordType,Address,Kind,LeftValue,RightValue," +
                    "Count,LeftMetric,RightMetric,Difference");

                var comparison = await _compareService.VisitAsync(
                    left,
                    right,
                    async (difference, token) =>
                    {
                        token.ThrowIfCancellationRequested();
                        await writer.WriteLineAsync(
                            string.Join(
                                ",",
                                "Difference",
                                $"0x{difference.Address:X16}",
                                difference.Kind.ToString(),
                                ToHex(difference.LeftValue),
                                ToHex(difference.RightValue),
                                string.Empty,
                                string.Empty,
                                string.Empty,
                                string.Empty));
                    },
                    progress,
                    cancellationToken);

                if (comparison.IsFailure)
                {
                    return Result<SnapshotComparisonSummary>.Failure(
                        comparison.Error);
                }

                await WriteSummaryAsync(
                    writer,
                    comparison.Value,
                    cancellationToken);
                await writer.FlushAsync(cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
                completedComparison = comparison;
            }

            File.Move(
                temporaryPath,
                fullPath,
                overwrite: true);
            return completedComparison;
        }
        catch (OperationCanceledException exception)
            when (cancellationToken.IsCancellationRequested)
        {
            return Result<SnapshotComparisonSummary>.Failure(
                new Error(
                    ErrorCode.Cancelled,
                    "Snapshot comparison export was cancelled.",
                    exception));
        }
        catch (Exception exception)
            when (exception is
                IOException or
                UnauthorizedAccessException or
                NotSupportedException or
                ArgumentException)
        {
            return Result<SnapshotComparisonSummary>.Failure(
                new Error(
                    ErrorCode.Io,
                    "Snapshot comparison could not be exported.",
                    exception));
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch
            {
                // The original export result is more actionable.
            }
        }
    }

    private static async Task WriteSummaryAsync(
        StreamWriter writer,
        SnapshotComparisonSummary summary,
        CancellationToken cancellationToken)
    {
        await WriteMetricAsync(
            writer,
            "Added",
            summary.AddedCount,
            cancellationToken);
        await WriteMetricAsync(
            writer,
            "Removed",
            summary.RemovedCount,
            cancellationToken);
        await WriteMetricAsync(
            writer,
            "Changed",
            summary.ChangedCount,
            cancellationToken);
        await WriteMetricAsync(
            writer,
            "Unchanged",
            summary.UnchangedCount,
            cancellationToken);
        await writer.WriteLineAsync(
            string.Join(
                ",",
                "Summary",
                string.Empty,
                "RecordCount",
                string.Empty,
                string.Empty,
                summary.TotalComparedAddressCount.ToString(
                    CultureInfo.InvariantCulture),
                summary.Left.RecordCount.ToString(
                    CultureInfo.InvariantCulture),
                summary.Right.RecordCount.ToString(
                    CultureInfo.InvariantCulture),
                summary.CountDifference.ToString(
                    CultureInfo.InvariantCulture)));
        await writer.WriteLineAsync(
            string.Join(
                ",",
                "Summary",
                string.Empty,
                "StorageBytes",
                string.Empty,
                string.Empty,
                string.Empty,
                summary.Left.PayloadLength.ToString(
                    CultureInfo.InvariantCulture),
                summary.Right.PayloadLength.ToString(
                    CultureInfo.InvariantCulture),
                summary.StorageSizeDifference.ToString(
                    CultureInfo.InvariantCulture)));
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static Task WriteMetricAsync(
        StreamWriter writer,
        string kind,
        long count,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return writer.WriteLineAsync(
            string.Join(
                ",",
                "Summary",
                string.Empty,
                kind,
                string.Empty,
                string.Empty,
                count.ToString(CultureInfo.InvariantCulture),
                string.Empty,
                string.Empty,
                string.Empty));
    }

    private static string ToHex(ReadOnlyMemory<byte>? value)
    {
        return value.HasValue
            ? Convert.ToHexString(value.Value.Span)
            : string.Empty;
    }

    private static Result<SnapshotComparisonSummary> Validation(
        string message)
    {
        return Result<SnapshotComparisonSummary>.Failure(
            new Error(ErrorCode.Validation, message));
    }
}
