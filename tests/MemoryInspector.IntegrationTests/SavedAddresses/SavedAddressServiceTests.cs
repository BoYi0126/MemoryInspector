using MemoryInspector.Application.Memory;
using MemoryInspector.Application.Monitoring;
using MemoryInspector.Application.SavedAddresses;
using MemoryInspector.Application.Scanning.Results;
using MemoryInspector.Common;
using MemoryInspector.Core.Monitoring;
using MemoryInspector.Core.Processes;
using MemoryInspector.Core.Scanning;
using MemoryInspector.IntegrationTests.ProcessExplorer;
using MemoryInspector.Wpf.Services;
using MemoryInspector.Wpf.ViewModels;

namespace MemoryInspector.IntegrationTests.SavedAddresses;

[TestClass]
public sealed class SavedAddressServiceTests
{
    private static readonly SavedAddressTarget Target = new(
        "Example.exe",
        ProcessArchitecture.X64);

    [TestMethod]
    public async Task CrudOperationsPersistBeforePublishingCatalog()
    {
        var store = new InMemorySavedAddressStore();
        using var service = new SavedAddressService(store);
        var changes = new List<SavedAddressCatalog>();
        service.CatalogChanged +=
            (_, eventArgs) => changes.Add(eventArgs.Catalog);
        _ = await service.InitializeAsync();

        var added = await service.AddAsync(
            Target,
            "Counter",
            0x1234,
            ScanValueType.Int32,
            "Initial");
        var renamed = await service.RenameAsync(
            "Counter",
            "Score");
        var updated = await service.UpdateAsync(
            "Score",
            ScanValueType.UInt64,
            "Updated");
        var deleted = await service.DeleteAsync("Score");

        Assert.IsTrue(added.IsSuccess);
        Assert.IsTrue(renamed.IsSuccess);
        Assert.IsTrue(updated.IsSuccess);
        Assert.IsTrue(deleted.IsSuccess);
        Assert.AreEqual(4, store.SaveCount);
        Assert.AreEqual(5, changes.Count);
        Assert.AreEqual(
            ScanValueType.UInt64,
            updated.Value.ValueType);
        Assert.AreEqual("Updated", updated.Value.Description);
        Assert.AreEqual(0, service.Catalog.Entries.Count);
        Assert.IsNull(service.Catalog.Target);
        Assert.AreSame(
            service.Catalog,
            store.Files[store.DefaultFilePath]);
    }

    [TestMethod]
    public async Task DuplicateKeyRequiresExplicitOverwrite()
    {
        var store = new InMemorySavedAddressStore();
        using var service = new SavedAddressService(store);
        _ = await service.InitializeAsync();
        _ = await service.AddAsync(
            Target,
            "Counter",
            0x1000,
            ScanValueType.Int32);
        var saveCount = store.SaveCount;

        var rejected = await service.AddAsync(
            Target,
            "counter",
            0x2000,
            ScanValueType.UInt16);
        var overwritten = await service.AddAsync(
            Target,
            "counter",
            0x2000,
            ScanValueType.UInt16,
            duplicateBehavior: DuplicateKeyBehavior.Overwrite);

        Assert.IsTrue(rejected.IsFailure);
        Assert.AreEqual(ErrorCode.Validation, rejected.Error.Code);
        Assert.AreEqual(saveCount + 1, store.SaveCount);
        Assert.IsTrue(overwritten.IsSuccess);
        Assert.AreEqual(1, service.Catalog.Entries.Count);
        Assert.AreEqual(
            0x2000UL,
            service.Catalog.Entries.Single().Address);
        Assert.AreEqual(
            ScanValueType.UInt16,
            service.Catalog.Entries.Single().ValueType);
    }

    [TestMethod]
    public async Task CatalogRejectsAddressesFromAnotherTarget()
    {
        var store = new InMemorySavedAddressStore();
        using var service = new SavedAddressService(store);
        _ = await service.InitializeAsync();
        _ = await service.AddAsync(
            Target,
            "Counter",
            0x1000,
            ScanValueType.Int32);

        var result = await service.AddAsync(
            new SavedAddressTarget(
                "Other.exe",
                ProcessArchitecture.X64),
            "Health",
            0x2000,
            ScanValueType.Int32);

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(ErrorCode.InvalidState, result.Error.Code);
        Assert.AreEqual(1, service.Catalog.Entries.Count);
        Assert.AreEqual("Counter", service.Catalog.Entries[0].Key);
    }

    [TestMethod]
    public async Task FailedImportLeavesCurrentCatalogUntouched()
    {
        var store = new InMemorySavedAddressStore();
        using var service = new SavedAddressService(store);
        _ = await service.InitializeAsync();
        _ = await service.AddAsync(
            Target,
            "Counter",
            0x1000,
            ScanValueType.Int32);
        var original = service.Catalog;
        store.LoadFailures["bad.json"] = new Error(
            ErrorCode.Serialization,
            "Invalid saved-address JSON.");

        var result = await service.ImportAsync("bad.json");

        Assert.IsTrue(result.IsFailure);
        Assert.AreSame(original, service.Catalog);
        Assert.AreEqual(
            "Counter",
            service.Catalog.Entries.Single().Key);
    }

    [TestMethod]
    public async Task FailedSaveDoesNotPublishOrChangeCatalog()
    {
        var store = new InMemorySavedAddressStore();
        using var service = new SavedAddressService(store);
        var changeCount = 0;
        service.CatalogChanged += (_, _) => changeCount++;
        _ = await service.InitializeAsync();
        store.SaveFailure = new Error(
            ErrorCode.Io,
            "The catalog could not be saved.");

        var result = await service.AddAsync(
            Target,
            "Counter",
            0x1000,
            ScanValueType.Int32);

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(ErrorCode.Io, result.Error.Code);
        Assert.AreEqual(0, service.Catalog.Entries.Count);
        Assert.AreEqual(1, changeCount);
    }

    [TestMethod]
    public async Task ImportReplacesDefaultCatalogAndExportUsesCurrentData()
    {
        var store = new InMemorySavedAddressStore();
        var imported = new SavedAddressCatalog(
            new SavedAddressTarget(
                "Imported.exe",
                ProcessArchitecture.Arm64),
            [
                new SavedAddressEntry(
                    "Player",
                    0xABC0,
                    ScanValueType.Double,
                    "Imported entry"),
            ]);
        store.Files["import.json"] = imported;
        using var service = new SavedAddressService(store);
        _ = await service.InitializeAsync();

        var import = await service.ImportAsync("import.json");
        var export = await service.ExportAsync("export.json");

        Assert.IsTrue(import.IsSuccess);
        Assert.IsTrue(export.IsSuccess);
        Assert.AreSame(
            imported,
            store.Files[store.DefaultFilePath]);
        Assert.AreSame(imported, store.Files["export.json"]);
        Assert.AreEqual(
            "Imported.exe",
            service.Catalog.Target!.ProcessName);
    }

    [TestMethod]
    public async Task ResultGridSaveActionUsesGeneratedKeyAndConfirmation()
    {
        var store = new InMemorySavedAddressStore();
        using var service = new SavedAddressService(store);
        var sessions = new ConnectedSessionService();
        var confirmations = new QueueConfirmationService(
            false,
            true);
        using var viewModel = new SavedAddressWindowViewModel(
            service,
            sessions,
            new SuccessfulMemoryReaderService(),
            confirmations,
            new StubJsonFileDialogService(),
            new TestLogger());
        await viewModel.InitializeAsync();
        var row = new ResultGridRowViewModel(
            new ResultGridItem(
                0x7FFF1234,
                ScanValueType.Int32,
                BitConverter.GetBytes(42),
                ResultReadStatus.Available));

        var first = await viewModel.AddFromResultAsync(row);
        var cancelledDuplicate =
            await viewModel.AddFromResultAsync(row);
        var overwritten =
            await viewModel.AddFromResultAsync(row);

        Assert.IsTrue(first.IsSuccess);
        Assert.IsTrue(cancelledDuplicate.IsFailure);
        Assert.AreEqual(
            ErrorCode.Cancelled,
            cancelledDuplicate.Error.Code);
        Assert.IsTrue(overwritten.IsSuccess);
        Assert.AreEqual(1, viewModel.Entries.Count);
        Assert.AreEqual(
            "Address_000000007FFF1234",
            viewModel.Entries[0].Key);
        Assert.AreEqual(2, confirmations.CallCount);
    }

    [TestMethod]
    public async Task ValidationUsesOneBatchAndIsolatesUnreadableAddress()
    {
        var store = new InMemorySavedAddressStore();
        store.Files[store.DefaultFilePath] =
            new SavedAddressCatalog(
                Target,
                [
                    new SavedAddressEntry(
                        "Readable",
                        0x1000,
                        ScanValueType.Int32),
                    new SavedAddressEntry(
                        "Unreadable",
                        0x2000,
                        ScanValueType.Int32),
                ]);
        using var service = new SavedAddressService(store);
        var reader = new SelectiveMemoryReaderService(
            unreadableIndex: 1);
        using var viewModel = new SavedAddressWindowViewModel(
            service,
            new ConnectedSessionService(),
            reader,
            new QueueConfirmationService(),
            new StubJsonFileDialogService(),
            new TestLogger());

        await viewModel.InitializeAsync();
        MemoryEditRequestedEventArgs? edited = null;
        viewModel.SelectedEntry = viewModel.Entries.Single(
            entry => entry.Key == "Readable");
        viewModel.EditValueRequested +=
            (_, eventArgs) => edited = eventArgs;
        viewModel.EditValueCommand.Execute(null);

        Assert.AreEqual(1, reader.BatchCallCount);
        Assert.AreEqual(
            SavedAddressReadStatus.Available,
            viewModel.Entries.Single(
                entry => entry.Key == "Readable").ReadStatus);
        Assert.AreEqual(
            "0",
            viewModel.Entries.Single(
                entry => entry.Key == "Readable")
                .CurrentValueDisplay);
        Assert.AreEqual(
            SavedAddressReadStatus.Unreadable,
            viewModel.Entries.Single(
                entry => entry.Key == "Unreadable").ReadStatus);
        Assert.AreEqual(0x1000UL, edited!.Address);
        Assert.AreEqual(
            MemoryInspector.Core.Memory.Editing.MemoryWriteSource.SavedAddress,
            edited.Source);
    }

    [TestMethod]
    public async Task ReconnectAutomaticallyRevalidatesSavedAddresses()
    {
        var store = new InMemorySavedAddressStore();
        store.Files[store.DefaultFilePath] =
            new SavedAddressCatalog(
                Target,
                [
                    new SavedAddressEntry(
                        "Counter",
                        0x1000,
                        ScanValueType.Int32),
                ]);
        using var service = new SavedAddressService(store);
        var sessions = new ConnectedSessionService();
        sessions.Disconnect();
        var reader = new SelectiveMemoryReaderService();
        using var viewModel = new SavedAddressWindowViewModel(
            service,
            sessions,
            reader,
            new QueueConfirmationService(),
            new StubJsonFileDialogService(),
            new TestLogger());
        await viewModel.InitializeAsync();
        Assert.AreEqual(
            SavedAddressReadStatus.TargetUnavailable,
            viewModel.Entries.Single().ReadStatus);

        sessions.Reconnect();
        await WaitUntilAsync(
            () => reader.BatchCallCount == 1);

        Assert.AreEqual(
            SavedAddressReadStatus.Available,
            viewModel.Entries.Single().ReadStatus);
    }

    [TestMethod]
    public async Task CorruptDefaultCatalogProducesVisibleErrorWithoutOverwrite()
    {
        var store = new InMemorySavedAddressStore();
        store.LoadFailures[store.DefaultFilePath] = new Error(
            ErrorCode.Serialization,
            "The saved-address JSON format is invalid.");
        using var service = new SavedAddressService(store);
        using var viewModel = new SavedAddressWindowViewModel(
            service,
            new ConnectedSessionService(),
            new SelectiveMemoryReaderService(),
            new QueueConfirmationService(),
            new StubJsonFileDialogService(),
            new TestLogger());

        await viewModel.InitializeAsync();

        Assert.IsNotNull(viewModel.ErrorMessage);
        Assert.IsTrue(
            viewModel.ErrorMessage.Contains(
                "could not be loaded",
                StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(0, store.SaveCount);
        Assert.AreEqual(0, viewModel.Entries.Count);
        Assert.IsTrue(store.LoadFailures.ContainsKey(
            store.DefaultFilePath));
    }

    private static async Task WaitUntilAsync(
        Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(2));

        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class InMemorySavedAddressStore :
        ISavedAddressStore
    {
        public string DefaultFilePath { get; } = "default.json";

        public Dictionary<string, SavedAddressCatalog> Files
        {
            get;
        } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, Error> LoadFailures
        {
            get;
        } = new(StringComparer.OrdinalIgnoreCase);

        public int SaveCount { get; private set; }

        public Error? SaveFailure { get; set; }

        public Task<Result<SavedAddressCatalog>> LoadAsync(
            string filePath,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (LoadFailures.TryGetValue(
                filePath,
                out var error))
            {
                return Task.FromResult(
                    Result<SavedAddressCatalog>.Failure(error));
            }

            return Task.FromResult(
                Files.TryGetValue(filePath, out var catalog)
                    ? Result<SavedAddressCatalog>.Success(catalog)
                    : Result<SavedAddressCatalog>.Failure(
                        new Error(
                            ErrorCode.NotFound,
                            "Saved addresses were not found.")));
        }

        public Task<Result> SaveAsync(
            SavedAddressCatalog catalog,
            string filePath,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (SaveFailure is not null)
            {
                return Task.FromResult(
                    Result.Failure(SaveFailure));
            }

            Files[filePath] = catalog;
            SaveCount++;
            return Task.FromResult(Result.Success());
        }
    }

    private sealed class ConnectedSessionService :
        IMonitoringSessionService
    {
        private readonly MonitoringSession _connectedSession =
            new()
            {
                SessionId = Guid.NewGuid(),
                Identity = new MonitoringSessionIdentity(
                    42,
                    new DateTimeOffset(
                        2026,
                        7,
                        29,
                        8,
                        30,
                        0,
                        TimeSpan.Zero),
                    ProcessArchitecture.X64,
                    "Example.exe"),
                State = MonitoringSessionState.Connected,
                CreatedAt = DateTimeOffset.UtcNow,
                ConnectedAt = DateTimeOffset.UtcNow,
            };

        public ConnectedSessionService()
        {
            CurrentSession = _connectedSession;
        }

        public MonitoringSession? CurrentSession { get; private set; }

        public event EventHandler<MonitoringSessionChangedEventArgs>?
            SessionChanged;

        public void Disconnect()
        {
            CurrentSession = _connectedSession with
            {
                State = MonitoringSessionState.Disconnected,
                EndedAt = DateTimeOffset.UtcNow,
                StatusMessage = "Disconnected.",
            };
        }

        public void Reconnect()
        {
            CurrentSession = _connectedSession with
            {
                State = MonitoringSessionState.Connected,
                ConnectedAt = DateTimeOffset.UtcNow,
            };
            SessionChanged?.Invoke(
                this,
                new MonitoringSessionChangedEventArgs(
                    CurrentSession));
        }

        public Task<Result<MonitoringSession>> StartAsync(
            MonitoringSessionIdentity identity,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<Result<MonitoringSession>> CheckLivenessAsync(
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<Result> StopAsync(
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SelectiveMemoryReaderService(
        int unreadableIndex = -1) : IMemoryReaderService
    {
        public int BatchCallCount { get; private set; }

        public Task<Result<MemoryReadResult>> ReadAsync(
            ulong address,
            int length,
            MemoryReadOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            throw new AssertFailedException(
                "Saved-address validation must use batch reads.");
        }

        public Task<Result<T>> TryReadAsync<T>(
            ulong address,
            MemoryReadOptions? options = null,
            CancellationToken cancellationToken = default)
            where T : unmanaged
        {
            throw new AssertFailedException(
                "Saved-address validation must use batch reads.");
        }

        public Task<Result<MemoryBatchReadResult>> ReadBatchAsync(
            IEnumerable<MemoryReadRequest> requests,
            MemoryReadOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            BatchCallCount++;
            var items = requests
                .Select((request, index) =>
                    index == unreadableIndex
                        ? new MemoryBatchReadItem(
                            request,
                            Result<MemoryReadResult>.Failure(
                                new Error(
                                    ErrorCode.NativeApi,
                                    "Address is unreadable.")))
                        : new MemoryBatchReadItem(
                            request,
                            Result<MemoryReadResult>.Success(
                                new MemoryReadResult(
                                    request,
                                    new byte[request.Length]))));
            return Task.FromResult(
                Result<MemoryBatchReadResult>.Success(
                    new MemoryBatchReadResult(items)));
        }
    }

    private sealed class SuccessfulMemoryReaderService :
        IMemoryReaderService
    {
        public Task<Result<MemoryReadResult>> ReadAsync(
            ulong address,
            int length,
            MemoryReadOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            throw new AssertFailedException(
                "Saved-address validation must use batch reads.");
        }

        public Task<Result<T>> TryReadAsync<T>(
            ulong address,
            MemoryReadOptions? options = null,
            CancellationToken cancellationToken = default)
            where T : unmanaged
        {
            throw new AssertFailedException(
                "Saved-address validation must use batch reads.");
        }

        public Task<Result<MemoryBatchReadResult>> ReadBatchAsync(
            IEnumerable<MemoryReadRequest> requests,
            MemoryReadOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var items = requests.Select(request =>
                new MemoryBatchReadItem(
                    request,
                    Result<MemoryReadResult>.Success(
                        new MemoryReadResult(
                            request,
                            new byte[request.Length]))));
            return Task.FromResult(
                Result<MemoryBatchReadResult>.Success(
                    new MemoryBatchReadResult(items)));
        }
    }

    private sealed class QueueConfirmationService(
        params bool[] responses) : IUserConfirmationService
    {
        private readonly Queue<bool> _responses = new(responses);

        public int CallCount { get; private set; }

        public bool Confirm(string title, string message)
        {
            CallCount++;
            return _responses.Dequeue();
        }
    }

    private sealed class StubJsonFileDialogService :
        IJsonFileDialogService
    {
        public string? SelectImportFile()
        {
            return null;
        }

        public string? SelectExportFile(string suggestedFileName)
        {
            return null;
        }
    }
}
