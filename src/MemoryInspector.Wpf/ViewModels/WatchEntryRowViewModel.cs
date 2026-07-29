using System.Buffers.Binary;
using System.Globalization;
using MemoryInspector.Application.Watch;
using MemoryInspector.Core.Scanning;

namespace MemoryInspector.Wpf.ViewModels;

public sealed class WatchEntryRowViewModel
{
    public WatchEntryRowViewModel(WatchEntry entry)
    {
        Entry = entry ??
            throw new ArgumentNullException(nameof(entry));
    }

    public WatchEntry Entry { get; }

    public bool IsStale => false;

    public bool IsUnreadable =>
        Status is WatchReadStatus.Unreadable or
            WatchReadStatus.TargetUnavailable;

    public Guid Key => Entry.Key;

    public string KeyDisplay =>
        Entry.Key.ToString("N")[..8].ToUpperInvariant();

    public ulong Address => Entry.Address;

    public string AddressDisplay => $"0x{Address:X16}";

    public ScanValueType ValueType => Entry.ValueType;

    public string ValueTypeDisplay => ValueType.ToString();

    public string PreviousValueDisplay =>
        Entry.PreviousValue.HasValue
            ? ResultGridRowViewModel.FormatValue(
                ValueType,
                Entry.PreviousValue.Value.Span)
            : "—";

    public string CurrentValueDisplay =>
        Entry.CurrentValue.HasValue
            ? ResultGridRowViewModel.FormatValue(
                ValueType,
                Entry.CurrentValue.Value.Span)
            : "—";

    public string DeltaDisplay =>
        Entry.PreviousValue.HasValue &&
        Entry.CurrentValue.HasValue
            ? FormatDelta(
                ValueType,
                Entry.PreviousValue.Value.Span,
                Entry.CurrentValue.Value.Span)
            : "—";

    public string LastUpdatedDisplay =>
        Entry.LastUpdatedAt.HasValue
            ? Entry.LastUpdatedAt.Value
                .ToLocalTime()
                .ToString(
                    "HH:mm:ss.fff",
                    CultureInfo.InvariantCulture)
            : "—";

    public WatchReadStatus Status => Entry.Status;

    public string StatusDisplay =>
        Entry.StatusMessage is null
            ? Entry.Status.ToString()
            : $"{Entry.Status}: {Entry.StatusMessage}";

    private static string FormatDelta(
        ScanValueType valueType,
        ReadOnlySpan<byte> previous,
        ReadOnlySpan<byte> current)
    {
        return valueType switch
        {
            ScanValueType.Byte =>
                ((decimal)current[0] - previous[0])
                    .ToString(CultureInfo.InvariantCulture),
            ScanValueType.Int16 =>
                ((decimal)BinaryPrimitives
                    .ReadInt16LittleEndian(current) -
                 BinaryPrimitives
                    .ReadInt16LittleEndian(previous))
                    .ToString(CultureInfo.InvariantCulture),
            ScanValueType.UInt16 =>
                ((decimal)BinaryPrimitives
                    .ReadUInt16LittleEndian(current) -
                 BinaryPrimitives
                    .ReadUInt16LittleEndian(previous))
                    .ToString(CultureInfo.InvariantCulture),
            ScanValueType.Int32 =>
                ((decimal)BinaryPrimitives
                    .ReadInt32LittleEndian(current) -
                 BinaryPrimitives
                    .ReadInt32LittleEndian(previous))
                    .ToString(CultureInfo.InvariantCulture),
            ScanValueType.UInt32 =>
                ((decimal)BinaryPrimitives
                    .ReadUInt32LittleEndian(current) -
                 BinaryPrimitives
                    .ReadUInt32LittleEndian(previous))
                    .ToString(CultureInfo.InvariantCulture),
            ScanValueType.Int64 =>
                ((decimal)BinaryPrimitives
                    .ReadInt64LittleEndian(current) -
                 BinaryPrimitives
                    .ReadInt64LittleEndian(previous))
                    .ToString(CultureInfo.InvariantCulture),
            ScanValueType.UInt64 =>
                ((decimal)BinaryPrimitives
                    .ReadUInt64LittleEndian(current) -
                 BinaryPrimitives
                    .ReadUInt64LittleEndian(previous))
                    .ToString(CultureInfo.InvariantCulture),
            ScanValueType.Float =>
                (BitConverter.ToSingle(current) -
                 BitConverter.ToSingle(previous))
                    .ToString("R", CultureInfo.InvariantCulture),
            ScanValueType.Double =>
                (BitConverter.ToDouble(current) -
                 BitConverter.ToDouble(previous))
                    .ToString("R", CultureInfo.InvariantCulture),
            _ => throw new ArgumentOutOfRangeException(
                nameof(valueType)),
        };
    }
}
