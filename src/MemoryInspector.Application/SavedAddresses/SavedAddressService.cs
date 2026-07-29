using MemoryInspector.Common;
using MemoryInspector.Core.Scanning;

namespace MemoryInspector.Application.SavedAddresses;

public sealed class SavedAddressService(
    ISavedAddressStore store) :
    ISavedAddressService,
    IDisposable
{
    private readonly ISavedAddressStore _store =
        Guard.NotNull(store);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SavedAddressCatalog _catalog =
        SavedAddressCatalog.Empty;
    private bool _initialized;
    private bool _disposed;

    public SavedAddressCatalog Catalog =>
        Volatile.Read(ref _catalog);

    public event EventHandler<SavedAddressesChangedEventArgs>?
        CatalogChanged;

    public async Task<Result<SavedAddressCatalog>> InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var wait = await WaitAsync(
            "Loading saved addresses was cancelled.",
            cancellationToken);

        if (wait.IsFailure)
        {
            return Result<SavedAddressCatalog>.Failure(wait.Error);
        }

        SavedAddressCatalog? changed = null;

        try
        {
            var result = await _store.LoadAsync(
                    _store.DefaultFilePath,
                    cancellationToken)
                .ConfigureAwait(false);

            if (result.IsFailure &&
                result.Error.Code != ErrorCode.NotFound)
            {
                return result;
            }

            changed = result.IsSuccess
                ? result.Value
                : SavedAddressCatalog.Empty;
            Volatile.Write(ref _catalog, changed);
            _initialized = true;
            return Result<SavedAddressCatalog>.Success(changed);
        }
        finally
        {
            _gate.Release();

            if (changed is not null)
            {
                PublishChanged(changed);
            }
        }
    }

    public async Task<Result<SavedAddressEntry>> AddAsync(
        SavedAddressTarget target,
        string key,
        ulong address,
        ScanValueType valueType,
        string? description = null,
        DuplicateKeyBehavior duplicateBehavior =
            DuplicateKeyBehavior.Reject,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        SavedAddressEntry entry;

        try
        {
            entry = new SavedAddressEntry(
                key,
                address,
                valueType,
                description);
        }
        catch (Exception exception)
            when (exception is ArgumentException or
                  ArgumentOutOfRangeException)
        {
            return Validation<SavedAddressEntry>(
                exception.Message,
                exception);
        }

        return await MutateAsync(
            catalog =>
            {
                if (catalog.Target is not null &&
                    !TargetsMatch(catalog.Target, target))
                {
                    return Result<Mutation<SavedAddressEntry>>.Failure(
                        new Error(
                            ErrorCode.InvalidState,
                            "The saved-address catalog belongs to " +
                            $"'{catalog.Target.ProcessName}' " +
                            $"({catalog.Target.Architecture})."));
                }

                var entries = catalog.Entries.ToList();
                var existingIndex = FindIndex(entries, entry.Key);

                if (existingIndex >= 0 &&
                    duplicateBehavior == DuplicateKeyBehavior.Reject)
                {
                    return Duplicate<SavedAddressEntry>(entry.Key);
                }

                if (existingIndex >= 0)
                {
                    entries[existingIndex] = entry;
                }
                else
                {
                    entries.Add(entry);
                }

                return Result<Mutation<SavedAddressEntry>>.Success(
                    new Mutation<SavedAddressEntry>(
                        new SavedAddressCatalog(target, entries),
                        entry));
            },
            cancellationToken);
    }

    public async Task<Result<SavedAddressEntry>> RenameAsync(
        string key,
        string newKey,
        DuplicateKeyBehavior duplicateBehavior =
            DuplicateKeyBehavior.Reject,
        CancellationToken cancellationToken = default)
    {
        return await MutateAsync(
            catalog =>
            {
                var entries = catalog.Entries.ToList();
                var sourceIndex = FindIndex(entries, key);

                if (sourceIndex < 0)
                {
                    return NotFound<SavedAddressEntry>(key);
                }

                SavedAddressEntry renamed;

                try
                {
                    var source = entries[sourceIndex];
                    renamed = new SavedAddressEntry(
                        newKey,
                        source.Address,
                        source.ValueType,
                        source.Description);
                }
                catch (Exception exception)
                    when (exception is ArgumentException or
                          ArgumentOutOfRangeException)
                {
                    return Validation<Mutation<SavedAddressEntry>>(
                        exception.Message,
                        exception);
                }

                var targetIndex = FindIndex(entries, renamed.Key);

                if (targetIndex >= 0 &&
                    targetIndex != sourceIndex &&
                    duplicateBehavior == DuplicateKeyBehavior.Reject)
                {
                    return Duplicate<SavedAddressEntry>(renamed.Key);
                }

                entries.RemoveAt(sourceIndex);
                targetIndex = FindIndex(entries, renamed.Key);

                if (targetIndex >= 0)
                {
                    entries[targetIndex] = renamed;
                }
                else
                {
                    entries.Add(renamed);
                }

                return Result<Mutation<SavedAddressEntry>>.Success(
                    new Mutation<SavedAddressEntry>(
                        new SavedAddressCatalog(
                            catalog.Target,
                            entries),
                        renamed));
            },
            cancellationToken);
    }

    public async Task<Result<SavedAddressEntry>> UpdateAsync(
        string key,
        ScanValueType valueType,
        string? description,
        CancellationToken cancellationToken = default)
    {
        return await MutateAsync(
            catalog =>
            {
                var entries = catalog.Entries.ToList();
                var index = FindIndex(entries, key);

                if (index < 0)
                {
                    return NotFound<SavedAddressEntry>(key);
                }

                SavedAddressEntry updated;

                try
                {
                    updated = new SavedAddressEntry(
                        entries[index].Key,
                        entries[index].Address,
                        valueType,
                        description);
                }
                catch (Exception exception)
                    when (exception is ArgumentException or
                          ArgumentOutOfRangeException)
                {
                    return Validation<Mutation<SavedAddressEntry>>(
                        exception.Message,
                        exception);
                }

                entries[index] = updated;
                return Result<Mutation<SavedAddressEntry>>.Success(
                    new Mutation<SavedAddressEntry>(
                        new SavedAddressCatalog(
                            catalog.Target,
                            entries),
                        updated));
            },
            cancellationToken);
    }

    public async Task<Result> DeleteAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        var result = await MutateAsync(
            catalog =>
            {
                var entries = catalog.Entries.ToList();
                var index = FindIndex(entries, key);

                if (index < 0)
                {
                    return NotFound<bool>(key);
                }

                entries.RemoveAt(index);
                var changed = entries.Count == 0
                    ? SavedAddressCatalog.Empty
                    : new SavedAddressCatalog(
                        catalog.Target,
                        entries);
                return Result<Mutation<bool>>.Success(
                    new Mutation<bool>(changed, true));
            },
            cancellationToken);
        return result.IsSuccess
            ? Result.Success()
            : Result.Failure(result.Error);
    }

    public async Task<Result<SavedAddressCatalog>> ImportAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return Validation<SavedAddressCatalog>(
                "An import file path is required.");
        }

        ObjectDisposedException.ThrowIf(_disposed, this);
        var loaded = await _store.LoadAsync(
                filePath,
                cancellationToken)
            .ConfigureAwait(false);

        if (loaded.IsFailure)
        {
            return loaded;
        }

        var wait = await WaitAsync(
            "Importing saved addresses was cancelled.",
            cancellationToken);

        if (wait.IsFailure)
        {
            return Result<SavedAddressCatalog>.Failure(wait.Error);
        }

        SavedAddressCatalog? changed = null;

        try
        {
            var save = await _store.SaveAsync(
                    loaded.Value,
                    _store.DefaultFilePath,
                    cancellationToken)
                .ConfigureAwait(false);

            if (save.IsFailure)
            {
                return Result<SavedAddressCatalog>.Failure(save.Error);
            }

            changed = loaded.Value;
            Volatile.Write(ref _catalog, changed);
            _initialized = true;
            return Result<SavedAddressCatalog>.Success(changed);
        }
        finally
        {
            _gate.Release();
            if (changed is not null)
            {
                PublishChanged(changed);
            }
        }
    }

    public async Task<Result> ExportAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return Validation("An export file path is required.");
        }

        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_initialized)
        {
            return InvalidState(
                "Saved addresses have not been initialized.");
        }

        return await _store.SaveAsync(
                Catalog,
                filePath,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _gate.Dispose();
        _disposed = true;
    }

    private async Task<Result<T>> MutateAsync<T>(
        Func<
            SavedAddressCatalog,
            Result<Mutation<T>>> createMutation,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var wait = await WaitAsync(
            "Saving saved addresses was cancelled.",
            cancellationToken);

        if (wait.IsFailure)
        {
            return Result<T>.Failure(wait.Error);
        }

        SavedAddressCatalog? changed = null;

        try
        {
            if (!_initialized)
            {
                return Result<T>.Failure(
                    new Error(
                        ErrorCode.InvalidState,
                        "Saved addresses have not been initialized."));
            }

            var mutation = createMutation(_catalog);

            if (mutation.IsFailure)
            {
                return Result<T>.Failure(mutation.Error);
            }

            var save = await _store.SaveAsync(
                    mutation.Value.Catalog,
                    _store.DefaultFilePath,
                    cancellationToken)
                .ConfigureAwait(false);

            if (save.IsFailure)
            {
                return Result<T>.Failure(save.Error);
            }

            changed = mutation.Value.Catalog;
            Volatile.Write(ref _catalog, changed);
            return Result<T>.Success(mutation.Value.Value);
        }
        finally
        {
            _gate.Release();

            if (changed is not null)
            {
                PublishChanged(changed);
            }
        }
    }

    private async Task<Result> WaitAsync(
        string cancelledMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            await _gate.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            return Result.Success();
        }
        catch (OperationCanceledException exception)
            when (cancellationToken.IsCancellationRequested)
        {
            return Result.Failure(
                new Error(
                    ErrorCode.Cancelled,
                    cancelledMessage,
                    exception));
        }
    }

    private void PublishChanged(SavedAddressCatalog catalog)
    {
        CatalogChanged?.Invoke(
            this,
            new SavedAddressesChangedEventArgs(catalog));
    }

    private static int FindIndex(
        IReadOnlyList<SavedAddressEntry> entries,
        string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return -1;
        }

        return entries
            .Select((entry, index) => (entry, index))
            .Where(pair => string.Equals(
                pair.entry.Key,
                key.Trim(),
                StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.index)
            .DefaultIfEmpty(-1)
            .First();
    }

    private static bool TargetsMatch(
        SavedAddressTarget left,
        SavedAddressTarget right)
    {
        return left.Architecture == right.Architecture &&
               string.Equals(
                   left.ProcessName,
                   right.ProcessName,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static Result<Mutation<T>> Duplicate<T>(string key)
    {
        return Result<Mutation<T>>.Failure(
            new Error(
                ErrorCode.Validation,
                $"Saved-address key '{key}' already exists."));
    }

    private static Result<Mutation<T>> NotFound<T>(string key)
    {
        return Result<Mutation<T>>.Failure(
            new Error(
                ErrorCode.NotFound,
                $"Saved-address key '{key}' was not found."));
    }

    private static Result Validation(string message)
    {
        return Result.Failure(
            new Error(ErrorCode.Validation, message));
    }

    private static Result<T> Validation<T>(
        string message,
        Exception? exception = null)
    {
        return Result<T>.Failure(
            new Error(
                ErrorCode.Validation,
                message,
                exception));
    }

    private static Result InvalidState(string message)
    {
        return Result.Failure(
            new Error(ErrorCode.InvalidState, message));
    }

    private sealed record Mutation<T>(
        SavedAddressCatalog Catalog,
        T Value);
}
