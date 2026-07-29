using MemoryInspector.Common;
using MemoryInspector.Core.Scanning;

namespace MemoryInspector.Application.SavedAddresses;

public interface ISavedAddressService
{
    SavedAddressCatalog Catalog { get; }

    event EventHandler<SavedAddressesChangedEventArgs>?
        CatalogChanged;

    Task<Result<SavedAddressCatalog>> InitializeAsync(
        CancellationToken cancellationToken = default);

    Task<Result<SavedAddressEntry>> AddAsync(
        SavedAddressTarget target,
        string key,
        ulong address,
        ScanValueType valueType,
        string? description = null,
        DuplicateKeyBehavior duplicateBehavior =
            DuplicateKeyBehavior.Reject,
        CancellationToken cancellationToken = default);

    Task<Result<SavedAddressEntry>> RenameAsync(
        string key,
        string newKey,
        DuplicateKeyBehavior duplicateBehavior =
            DuplicateKeyBehavior.Reject,
        CancellationToken cancellationToken = default);

    Task<Result<SavedAddressEntry>> UpdateAsync(
        string key,
        ScanValueType valueType,
        string? description,
        CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(
        string key,
        CancellationToken cancellationToken = default);

    Task<Result<SavedAddressCatalog>> ImportAsync(
        string filePath,
        CancellationToken cancellationToken = default);

    Task<Result> ExportAsync(
        string filePath,
        CancellationToken cancellationToken = default);
}
