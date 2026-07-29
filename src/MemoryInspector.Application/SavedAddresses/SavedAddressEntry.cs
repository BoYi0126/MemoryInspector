using MemoryInspector.Common;
using MemoryInspector.Core.Scanning;

namespace MemoryInspector.Application.SavedAddresses;

public sealed record SavedAddressEntry
{
    public const int MaximumKeyLength = 128;
    public const int MaximumDescriptionLength = 1_024;

    public SavedAddressEntry(
        string key,
        ulong address,
        ScanValueType valueType,
        string? description = null)
    {
        var normalizedKey =
            Guard.NotNullOrWhiteSpace(key).Trim();

        if (normalizedKey.Length > MaximumKeyLength)
        {
            throw new ArgumentException(
                $"Saved-address keys cannot exceed " +
                $"{MaximumKeyLength} characters.",
                nameof(key));
        }

        _ = ScanValueTypeInfo.GetSize(valueType);
        var normalizedDescription =
            string.IsNullOrWhiteSpace(description)
                ? null
                : description.Trim();

        if (normalizedDescription?.Length >
            MaximumDescriptionLength)
        {
            throw new ArgumentException(
                $"Saved-address descriptions cannot exceed " +
                $"{MaximumDescriptionLength} characters.",
                nameof(description));
        }

        Key = normalizedKey;
        Address = address;
        ValueType = valueType;
        Description = normalizedDescription;
    }

    public string Key { get; }

    public ulong Address { get; }

    public ScanValueType ValueType { get; }

    public string? Description { get; }
}
