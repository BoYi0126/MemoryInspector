namespace MemoryInspector.Application.SavedAddresses;

public sealed class SavedAddressCatalog
{
    public const int CurrentSchemaVersion = 1;
    private readonly SavedAddressEntry[] _entries;

    public SavedAddressCatalog(
        SavedAddressTarget? target,
        IEnumerable<SavedAddressEntry>? entries = null)
    {
        _entries = entries?.ToArray() ?? [];

        if (_entries.Any(entry => entry is null))
        {
            throw new ArgumentException(
                "Saved-address entries cannot contain null.",
                nameof(entries));
        }

        var duplicate = _entries
            .GroupBy(
                entry => entry.Key,
                StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"Saved-address key '{duplicate.Key}' is duplicated.",
                nameof(entries));
        }

        if (_entries.Length > 0 && target is null)
        {
            throw new ArgumentException(
                "A non-empty saved-address catalog requires a target.",
                nameof(target));
        }

        Target = target;
    }

    public int SchemaVersion => CurrentSchemaVersion;

    public SavedAddressTarget? Target { get; }

    public IReadOnlyList<SavedAddressEntry> Entries =>
        Array.AsReadOnly(_entries);

    public static SavedAddressCatalog Empty { get; } =
        new(null);
}
