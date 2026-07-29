using System.Globalization;
using System.Text;
using MemoryInspector.Application.Memory.Editing;
using MemoryInspector.Common;
using MemoryInspector.Core.Memory.Editing;

namespace MemoryInspector.Windows.Memory.Editing;

public sealed class CsvMemoryWriteAuditExportService :
    IMemoryWriteAuditExportService
{
    public async Task<Result> ExportSummaryAsync(
        string path,
        IReadOnlyList<MemoryWriteAuditEntry> entries,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Result.Failure(
                new Error(
                    ErrorCode.Validation,
                    "An audit export path is required."));
        }

        ArgumentNullException.ThrowIfNull(entries);

        try
        {
            var fullPath = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(fullPath);

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var stream = new FileStream(
                fullPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                4_096,
                FileOptions.Asynchronous);
            await using var writer = new StreamWriter(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            await writer.WriteLineAsync(
                "Time,Process,PID,Address,Type,Original," +
                "Requested,ReadBack,Result,Verification," +
                "Failure,Source,Note");

            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var values = new[]
                {
                    entry.Timestamp.ToString(
                        "O",
                        CultureInfo.InvariantCulture),
                    entry.TargetIdentity.ProcessName,
                    entry.TargetIdentity.ProcessId.ToString(
                        CultureInfo.InvariantCulture),
                    $"0x{entry.Address:X16}",
                    entry.ValueType.ToString(),
                    ToHex(entry.OriginalValue),
                    Convert.ToHexString(entry.RequestedValue.Span),
                    ToHex(entry.ReadBackValue),
                    entry.Success ? "Success" : "Failure",
                    entry.VerificationStatus.ToString(),
                    entry.FailureReason.ToString(),
                    entry.Source.ToString(),
                    entry.UserNote ?? string.Empty,
                };
                await writer.WriteLineAsync(
                    string.Join(",", values.Select(Escape)));
            }

            await writer.FlushAsync(cancellationToken);
            return Result.Success();
        }
        catch (OperationCanceledException exception)
            when (cancellationToken.IsCancellationRequested)
        {
            return Result.Failure(
                new Error(
                    ErrorCode.Cancelled,
                    "Audit summary export was cancelled.",
                    exception));
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            NotSupportedException or
            ArgumentException)
        {
            return Result.Failure(
                new Error(
                    ErrorCode.Io,
                    "Audit summary could not be exported.",
                    exception));
        }
    }

    private static string ToHex(ReadOnlyMemory<byte>? value)
    {
        return value.HasValue
            ? Convert.ToHexString(value.Value.Span)
            : string.Empty;
    }

    private static string Escape(string value)
    {
        return value.IndexOfAny([',', '"', '\r', '\n']) < 0
            ? value
            : $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
