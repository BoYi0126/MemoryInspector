namespace MemoryInspector.Application.SavedAddresses;

public sealed class SavedAddressesChangedEventArgs(
    SavedAddressCatalog catalog) : EventArgs
{
    public SavedAddressCatalog Catalog { get; } =
        catalog ?? throw new ArgumentNullException(nameof(catalog));
}
