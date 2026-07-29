using System.Globalization;
using MemoryInspector.Core.Memory.Editing;

namespace MemoryInspector.Wpf.ViewModels;

public sealed class MemoryWriteHistoryRowViewModel(
    MemoryWriteAuditEntry entry)
{
    public MemoryWriteAuditEntry Entry { get; } =
        entry ?? throw new ArgumentNullException(nameof(entry));

    public string TimeDisplay => Entry.Timestamp
        .ToLocalTime()
        .ToString(
            "yyyy-MM-dd HH:mm:ss.fff",
            CultureInfo.InvariantCulture);

    public string ProcessDisplay =>
        $"{Entry.TargetIdentity.ProcessName} " +
        $"({Entry.TargetIdentity.ProcessId})";

    public string AddressDisplay => $"0x{Entry.Address:X16}";

    public string TypeDisplay => Entry.ValueType.ToString();

    public string OriginalDisplay => ToHex(Entry.OriginalValue);

    public string RequestedDisplay =>
        Convert.ToHexString(Entry.RequestedValue.Span);

    public string ReadBackDisplay => ToHex(Entry.ReadBackValue);

    public string ResultDisplay => Entry.Success
        ? $"Success · {Entry.VerificationStatus}"
        : $"Failure · {Entry.FailureReason}";

    public string SourceDisplay => Entry.Source.ToString();

    public string NoteDisplay => Entry.UserNote ?? string.Empty;

    public string CopySummary =>
        $"{TimeDisplay}\t{ProcessDisplay}\t{AddressDisplay}\t" +
        $"{TypeDisplay}\t{OriginalDisplay}\t{RequestedDisplay}\t" +
        $"{ReadBackDisplay}\t{ResultDisplay}\t{SourceDisplay}\t" +
        $"{NoteDisplay}";

    public bool Matches(string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        var text = filter.Trim();
        return ProcessDisplay.Contains(
                   text,
                   StringComparison.OrdinalIgnoreCase) ||
               AddressDisplay.Contains(
                   text,
                   StringComparison.OrdinalIgnoreCase) ||
               TypeDisplay.Contains(
                   text,
                   StringComparison.OrdinalIgnoreCase) ||
               ResultDisplay.Contains(
                   text,
                   StringComparison.OrdinalIgnoreCase) ||
               SourceDisplay.Contains(
                   text,
                   StringComparison.OrdinalIgnoreCase) ||
               NoteDisplay.Contains(
                   text,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string ToHex(ReadOnlyMemory<byte>? value)
    {
        return value.HasValue
            ? Convert.ToHexString(value.Value.Span)
            : "—";
    }
}
