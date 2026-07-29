using MemoryInspector.Application.SavedAddresses;
using MemoryInspector.Core.Scanning;

namespace MemoryInspector.Wpf.ViewModels;

public sealed class SavedAddressRowViewModel(
    SavedAddressEntry entry,
    SavedAddressReadStatus readStatus =
        SavedAddressReadStatus.Unverified,
    string? statusMessage = null,
    ReadOnlyMemory<byte>? currentValue = null)
{
    private readonly byte[]? _currentValue =
        currentValue?.ToArray();

    public SavedAddressEntry Entry { get; } =
        entry ?? throw new ArgumentNullException(nameof(entry));

    public string Key => Entry.Key;

    public ulong Address => Entry.Address;

    public string AddressDisplay => $"0x{Address:X16}";

    public ScanValueType ValueType => Entry.ValueType;

    public string ValueTypeDisplay => ValueType.ToString();

    public string Description => Entry.Description ?? string.Empty;

    public string CurrentValueDisplay =>
        _currentValue is null
            ? "—"
            : ResultGridRowViewModel.FormatValue(
                ValueType,
                _currentValue);

    public SavedAddressReadStatus ReadStatus { get; } = readStatus;

    public string StatusDisplay { get; } =
        string.IsNullOrWhiteSpace(statusMessage)
            ? readStatus.ToString()
            : $"{readStatus}: {statusMessage.Trim()}";

    public bool IsUnreadable =>
        ReadStatus is SavedAddressReadStatus.Unreadable or
            SavedAddressReadStatus.TargetMismatch or
            SavedAddressReadStatus.TargetUnavailable;
}
