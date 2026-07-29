using System.Text.Json;
using MemoryInspector.Application.SavedAddresses;
using MemoryInspector.Common;
using MemoryInspector.Core.Processes;
using MemoryInspector.Core.Scanning;
using MemoryInspector.Windows.Configuration;
using MemoryInspector.Windows.SavedAddresses;
using MemoryInspector.Windows.Tests.Configuration;

namespace MemoryInspector.Windows.Tests.SavedAddresses;

[TestClass]
public sealed class JsonSavedAddressStoreTests
{
    [TestMethod]
    public async Task SaveWritesSchemaVersionedPortableJsonAndRoundTrips()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var paths = new AppPathService(
            Path.Combine(temporaryDirectory.RootPath, "AppData"));
        var store = new JsonSavedAddressStore(paths);
        var catalog = new SavedAddressCatalog(
            new SavedAddressTarget(
                "Example.exe",
                ProcessArchitecture.X64),
            [
                new SavedAddressEntry(
                    "Counter",
                    0x12345678,
                    ScanValueType.Int32,
                    "Counter value"),
            ]);

        var save = await store.SaveAsync(
            catalog,
            store.DefaultFilePath);
        var load = await store.LoadAsync(store.DefaultFilePath);

        Assert.IsTrue(save.IsSuccess);
        Assert.IsTrue(load.IsSuccess);
        Assert.AreEqual(
            paths.SavedAddressesDirectory,
            Path.GetDirectoryName(store.DefaultFilePath));
        Assert.AreEqual(
            "Example.exe",
            load.Value.Target!.ProcessName);
        Assert.AreEqual(
            ProcessArchitecture.X64,
            load.Value.Target.Architecture);
        Assert.AreEqual(1, load.Value.Entries.Count);
        Assert.AreEqual(
            0x12345678UL,
            load.Value.Entries[0].Address);
        Assert.AreEqual(
            ScanValueType.Int32,
            load.Value.Entries[0].ValueType);

        using var document = JsonDocument.Parse(
            await File.ReadAllTextAsync(store.DefaultFilePath));
        Assert.AreEqual(
            1,
            document.RootElement
                .GetProperty("schemaVersion")
                .GetInt32());
        Assert.AreEqual(
            "x64",
            document.RootElement
                .GetProperty("target")
                .GetProperty("architecture")
                .GetString());
        Assert.AreEqual(
            "0x0000000012345678",
            document.RootElement
                .GetProperty("addresses")
                .GetProperty("Counter")
                .GetProperty("address")
                .GetString());
    }

    [TestMethod]
    public async Task InvalidJsonAndUnsupportedSchemaReturnSerializationError()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var paths = new AppPathService(temporaryDirectory.RootPath);
        var store = new JsonSavedAddressStore(paths);
        _ = paths.EnsureDirectories();
        var malformed = Path.Combine(
            paths.SavedAddressesDirectory,
            "malformed.json");
        var unsupported = Path.Combine(
            paths.SavedAddressesDirectory,
            "unsupported.json");
        await File.WriteAllTextAsync(
            malformed,
            """{"schemaVersion":1,"addresses":""");
        await File.WriteAllTextAsync(
            unsupported,
            """
            {
              "schemaVersion": 2,
              "target": {
                "processName": "Example.exe",
                "architecture": "x64"
              },
              "addresses": {}
            }
            """);

        var malformedResult = await store.LoadAsync(malformed);
        var unsupportedResult = await store.LoadAsync(unsupported);

        Assert.IsTrue(malformedResult.IsFailure);
        Assert.AreEqual(
            ErrorCode.Serialization,
            malformedResult.Error.Code);
        Assert.IsTrue(unsupportedResult.IsFailure);
        Assert.AreEqual(
            ErrorCode.Serialization,
            unsupportedResult.Error.Code);
    }

    [TestMethod]
    public async Task SavedCatalogSurvivesTemporaryScanDataDeletion()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var paths = new AppPathService(temporaryDirectory.RootPath);
        var store = new JsonSavedAddressStore(paths);
        var catalog = new SavedAddressCatalog(
            new SavedAddressTarget(
                "Persistent.exe",
                ProcessArchitecture.X64),
            [
                new SavedAddressEntry(
                    "Health",
                    0x4000,
                    ScanValueType.Int32),
            ]);
        _ = await store.SaveAsync(
            catalog,
            store.DefaultFilePath);
        await File.WriteAllTextAsync(
            Path.Combine(paths.TempDirectory, "scan.tmp"),
            "temporary");

        Directory.Delete(paths.TempDirectory, recursive: true);
        var load = await store.LoadAsync(store.DefaultFilePath);

        Assert.IsTrue(load.IsSuccess);
        Assert.AreEqual("Health", load.Value.Entries.Single().Key);
        Assert.IsFalse(Directory.Exists(paths.TempDirectory));
        Assert.IsFalse(
            Directory.EnumerateFiles(
                paths.SavedAddressesDirectory,
                "*.tmp-*")
                .Any());
    }

    [TestMethod]
    public async Task InvalidAddressAndDuplicateKeysAreRejectedClearly()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var paths = new AppPathService(temporaryDirectory.RootPath);
        var store = new JsonSavedAddressStore(paths);
        _ = paths.EnsureDirectories();
        var file = Path.Combine(
            paths.SavedAddressesDirectory,
            "invalid.json");
        var duplicateFile = Path.Combine(
            paths.SavedAddressesDirectory,
            "duplicate.json");
        await File.WriteAllTextAsync(
            file,
            """
            {
              "schemaVersion": 1,
              "target": {
                "processName": "Example.exe",
                "architecture": "x64"
              },
              "addresses": {
                "Counter": {
                  "address": "1234",
                  "valueType": "Int32"
                }
              }
            }
            """);
        await File.WriteAllTextAsync(
            duplicateFile,
            """
            {
              "schemaVersion": 1,
              "target": {
                "processName": "Example.exe",
                "architecture": "x64"
              },
              "addresses": {
                "Counter": {
                  "address": "0x0000000000001234",
                  "valueType": "Int32"
                },
                "counter": {
                  "address": "0x0000000000001234",
                  "valueType": "Int32"
                }
              }
            }
            """);

        var result = await store.LoadAsync(file);
        var duplicateResult = await store.LoadAsync(duplicateFile);

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(
            ErrorCode.Serialization,
            result.Error.Code);
        Assert.IsTrue(
            result.Error.ToDisplayMessage().Contains(
                "invalid",
                StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(duplicateResult.IsFailure);
        Assert.AreEqual(
            ErrorCode.Serialization,
            duplicateResult.Error.Code);
    }
}
