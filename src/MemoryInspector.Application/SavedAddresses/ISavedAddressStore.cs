using MemoryInspector.Common;

namespace MemoryInspector.Application.SavedAddresses;

public interface ISavedAddressStore
{
    string DefaultFilePath { get; }

    Task<Result<SavedAddressCatalog>> LoadAsync(
        string filePath,
        CancellationToken cancellationToken = default);

    Task<Result> SaveAsync(
        SavedAddressCatalog catalog,
        string filePath,
        CancellationToken cancellationToken = default);
}
