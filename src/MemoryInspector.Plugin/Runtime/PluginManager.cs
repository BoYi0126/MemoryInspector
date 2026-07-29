using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using MemoryInspector.Common;

namespace MemoryInspector.Plugin.Runtime;

public sealed class PluginManager :
    IPluginManager,
    IAsyncDisposable
{
    private const string ManifestFileName = "plugin.json";
    private const string StateFileName = ".plugin-state.json";
    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            UnmappedMemberHandling =
                JsonUnmappedMemberHandling.Disallow,
            Converters =
            {
                new JsonStringEnumConverter(
                    JsonNamingPolicy.CamelCase),
            },
        };
    private readonly object _sync = new();
    private readonly string _pluginLogsDirectory;
    private readonly Version _hostVersion;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, PluginEntry> _entries =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _activation =
        new(StringComparer.OrdinalIgnoreCase);
    private PluginManagerSnapshot _snapshot =
        new([], 0, 0, 0, 0, 0);
    private bool _disposed;

    public PluginManager(
        string pluginsDirectory,
        string pluginLogsDirectory,
        Version? hostVersion = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            pluginsDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            pluginLogsDirectory);
        PluginsDirectory = Path.GetFullPath(pluginsDirectory);
        _pluginLogsDirectory =
            Path.GetFullPath(pluginLogsDirectory);
        _hostVersion = hostVersion ??
            PluginApiVersion.HostVersion;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public string PluginsDirectory { get; }

    public PluginManagerSnapshot CurrentSnapshot
    {
        get
        {
            lock (_sync)
            {
                return _snapshot;
            }
        }
    }

    public Task<Result<PluginManagerSnapshot>> InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        return RefreshAsync(cancellationToken);
    }

    public async Task<Result<PluginManagerSnapshot>> RefreshAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var entered = await EnterAsync(cancellationToken)
            .ConfigureAwait(false);

        if (entered.IsFailure)
        {
            return Result<PluginManagerSnapshot>.Failure(
                entered.Error);
        }

        try
        {
            await UnloadAllAsync(CancellationToken.None)
                .ConfigureAwait(false);
            Directory.CreateDirectory(PluginsDirectory);
            Directory.CreateDirectory(_pluginLogsDirectory);
            var state = await LoadActivationStateAsync(
                    cancellationToken)
                .ConfigureAwait(false);

            if (state.IsFailure)
            {
                return Result<PluginManagerSnapshot>.Failure(
                    state.Error);
            }

            _activation.Clear();

            foreach (var pair in state.Value)
            {
                _activation[pair.Key] = pair.Value;
            }

            var discovered = await DiscoverAsync(
                    cancellationToken)
                .ConfigureAwait(false);
            var duplicateIds = discovered
                .Where(item => item.Manifest is not null)
                .GroupBy(
                    item => item.Manifest!.Id,
                    StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            _entries.Clear();

            foreach (var item in discovered)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = CreateEntry(
                    item,
                    duplicateIds);
                _entries[entry.Key] = entry;

                if (entry.Manifest is null ||
                    entry.Descriptor.State is
                        PluginLoadState.Failed or
                        PluginLoadState.Incompatible ||
                    !entry.Descriptor.IsEnabled)
                {
                    continue;
                }

                await LoadEntryAsync(
                        entry,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            return Result<PluginManagerSnapshot>.Success(
                PublishSnapshot());
        }
        catch (Exception exception)
        {
            return Failure<PluginManagerSnapshot>(
                exception,
                "Plugin discovery failed.",
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<Result<PluginManagerSnapshot>> EnableAsync(
        string pluginId,
        CancellationToken cancellationToken = default)
    {
        return SetEnabledAsync(
            pluginId,
            enabled: true,
            cancellationToken);
    }

    public Task<Result<PluginManagerSnapshot>> DisableAsync(
        string pluginId,
        CancellationToken cancellationToken = default)
    {
        return SetEnabledAsync(
            pluginId,
            enabled: false,
            cancellationToken);
    }

    public IReadOnlyList<IPluginUiContribution>
        GetUiContributions()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_sync)
        {
            return _entries.Values
                .Where(entry => entry.Runtime is not null)
                .SelectMany(entry =>
                    entry.Runtime!.Contributions)
                .ToArray();
        }
    }

    public Result OpenPluginsFolder()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            Directory.CreateDirectory(PluginsDirectory);
            _ = Process.Start(
                new ProcessStartInfo
                {
                    FileName = PluginsDirectory,
                    UseShellExecute = true,
                });
            return Result.Success();
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidOperationException or
            System.ComponentModel.Win32Exception)
        {
            return Result.Failure(
                new Error(
                    ErrorCode.Io,
                    "The plugin folder could not be opened.",
                    exception));
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _gate.WaitAsync().ConfigureAwait(false);

        try
        {
            await UnloadAllAsync(CancellationToken.None)
                .ConfigureAwait(false);
            _entries.Clear();
            _disposed = true;
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private async Task<Result<PluginManagerSnapshot>>
        SetEnabledAsync(
            string pluginId,
            bool enabled,
            CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(pluginId))
        {
            return Validation<PluginManagerSnapshot>(
                "Plugin ID is required.");
        }

        ObjectDisposedException.ThrowIf(_disposed, this);
        var entered = await EnterAsync(cancellationToken)
            .ConfigureAwait(false);

        if (entered.IsFailure)
        {
            return Result<PluginManagerSnapshot>.Failure(
                entered.Error);
        }

        try
        {
            if (!_entries.TryGetValue(pluginId, out var entry) ||
                entry.Manifest is null)
            {
                return Result<PluginManagerSnapshot>.Failure(
                    new Error(
                        ErrorCode.NotFound,
                        "Plugin was not found."));
            }

            if (entry.Descriptor.State ==
                PluginLoadState.Incompatible)
            {
                return Result<PluginManagerSnapshot>.Failure(
                    new Error(
                        ErrorCode.InvalidState,
                        entry.Descriptor.ErrorMessage ??
                        "Plugin is incompatible."));
            }

            _activation[entry.Manifest.Id] = enabled;
            var save = await SaveActivationStateAsync(
                    cancellationToken)
                .ConfigureAwait(false);

            if (save.IsFailure)
            {
                return Result<PluginManagerSnapshot>.Failure(
                    save.Error);
            }

            if (!enabled)
            {
                await UnloadEntryAsync(
                        entry,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                entry.Descriptor = ToDescriptor(
                    entry.Manifest,
                    entry.Directory,
                    PluginLoadState.Disabled,
                    isEnabled: false);
            }
            else if (entry.Runtime is null)
            {
                entry.Descriptor = ToDescriptor(
                    entry.Manifest,
                    entry.Directory,
                    PluginLoadState.Disabled,
                    isEnabled: true);
                await LoadEntryAsync(entry, cancellationToken)
                    .ConfigureAwait(false);
            }

            return Result<PluginManagerSnapshot>.Success(
                PublishSnapshot());
        }
        catch (Exception exception)
        {
            return Failure<PluginManagerSnapshot>(
                exception,
                enabled
                    ? "Plugin could not be enabled."
                    : "Plugin could not be disabled.",
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<DiscoveredPlugin>> DiscoverAsync(
        CancellationToken cancellationToken)
    {
        var result = new List<DiscoveredPlugin>();

        foreach (var directory in Directory
                     .EnumerateDirectories(
                         PluginsDirectory,
                         "*",
                         SearchOption.TopDirectoryOnly)
                     .Order(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var manifestPath = Path.Combine(
                directory,
                ManifestFileName);

            if (!File.Exists(manifestPath))
            {
                continue;
            }

            try
            {
                await using var stream = new FileStream(
                    manifestPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 16 * 1024,
                    FileOptions.Asynchronous |
                    FileOptions.SequentialScan);
                var manifest =
                    await JsonSerializer
                        .DeserializeAsync<PluginManifest>(
                            stream,
                            JsonOptions,
                            cancellationToken)
                        .ConfigureAwait(false);

                if (manifest is null)
                {
                    throw new InvalidDataException(
                        "Plugin manifest was empty.");
                }

                var validation = manifest.Validate(
                    _hostVersion,
                    PluginApiVersion.Current);
                result.Add(
                    validation.IsSuccess
                        ? new DiscoveredPlugin(
                            directory,
                            manifest,
                            null,
                            PluginLoadState.Disabled)
                        : new DiscoveredPlugin(
                            directory,
                            manifest,
                            validation.Error.ToDisplayMessage(),
                            validation.Error.Code ==
                                ErrorCode.InvalidState
                                ? PluginLoadState.Incompatible
                                : PluginLoadState.Failed));
            }
            catch (Exception exception) when (
                exception is JsonException or
                IOException or
                UnauthorizedAccessException or
                InvalidDataException or
                NotSupportedException)
            {
                var id = Path.GetFileName(directory);
                var logger = CreateLogger(id);
                _ = logger.Log(
                    PluginLogLevel.Error,
                    "Plugin manifest could not be read.",
                    exception);
                result.Add(
                    new DiscoveredPlugin(
                        directory,
                        null,
                        exception.Message,
                        PluginLoadState.Failed));
            }
        }

        return result;
    }

    private PluginEntry CreateEntry(
        DiscoveredPlugin item,
        IReadOnlySet<string> duplicateIds)
    {
        var manifest = item.Manifest;
        var key = manifest?.Id ??
            $"invalid:{Path.GetFileName(item.Directory)}";

        if (manifest is not null &&
            duplicateIds.Contains(manifest.Id))
        {
            return new PluginEntry(
                manifest.Id,
                item.Directory,
                manifest,
                ToDescriptor(
                    manifest,
                    item.Directory,
                    PluginLoadState.Failed,
                    isEnabled: false,
                    errorMessage:
                        "Duplicate plugin ID was discovered."));
        }

        if (manifest is null)
        {
            return new PluginEntry(
                key,
                item.Directory,
                null,
                new PluginDescriptor(
                    key,
                    Path.GetFileName(item.Directory),
                    string.Empty,
                    [],
                    PluginLoadState.Failed,
                    false,
                    false,
                    0,
                    item.Directory,
                    ErrorMessage: item.ErrorMessage));
        }

        var enabled = _activation.TryGetValue(
                manifest.Id,
                out var overrideValue)
            ? overrideValue
            : manifest.EnabledByDefault;
        var state = item.State is
            PluginLoadState.Failed or
            PluginLoadState.Incompatible
                ? item.State
                : PluginLoadState.Disabled;
        return new PluginEntry(
            key,
            item.Directory,
            manifest,
            ToDescriptor(
                manifest,
                item.Directory,
                state,
                state is PluginLoadState.Failed or
                    PluginLoadState.Incompatible
                    ? false
                    : enabled,
                item.ErrorMessage));
    }

    private async Task LoadEntryAsync(
        PluginEntry entry,
        CancellationToken cancellationToken)
    {
        var manifest = entry.Manifest!;
        PluginLoadContext? loadContext = null;
        PluginRuntime? runtime = null;
        var logger = CreateLogger(manifest.Id);

        try
        {
            var assemblyPath = Path.GetFullPath(
                Path.Combine(
                    entry.Directory,
                    manifest.EntryAssembly));
            var root = Path.GetFullPath(entry.Directory)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;

            if (!assemblyPath.StartsWith(
                    root,
                    StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(assemblyPath))
            {
                throw new FileNotFoundException(
                    "Plugin entry assembly was not found.",
                    assemblyPath);
            }

            loadContext = new PluginLoadContext(assemblyPath);
            var assembly = loadContext.LoadEntryAssembly(
                assemblyPath);
            var type = assembly.GetType(
                manifest.EntryType,
                throwOnError: true,
                ignoreCase: false)!;

            if (!typeof(IMemoryInspectorPlugin)
                    .IsAssignableFrom(type) ||
                type.IsAbstract ||
                type.GetConstructor(Type.EmptyTypes) is null)
            {
                throw new InvalidDataException(
                    "Plugin entry type must be a concrete public " +
                    "IMemoryInspectorPlugin with a parameterless " +
                    "constructor.");
            }

            var module =
                (IMemoryInspectorPlugin)Activator.CreateInstance(type)!;
            var context = new PluginContext(
                manifest.Id,
                entry.Directory,
                _hostVersion,
                logger);
            var services = new ServiceCollection();
            services.AddSingleton<IPluginContext>(context);
            services.AddSingleton<IPluginLogger>(logger);
            services.AddSingleton(_timeProvider);
            module.ConfigureServices(services);
            var provider = services.BuildServiceProvider(
                new ServiceProviderOptions
                {
                    ValidateOnBuild = true,
                    ValidateScopes = true,
                });
            context.SetServices(provider);
            runtime = new PluginRuntime(
                loadContext,
                module,
                provider,
                []);
            loadContext = null;
            await module.InitializeAsync(
                    context,
                    cancellationToken)
                .AsTask()
                .WaitAsync(
                    TimeSpan.FromSeconds(10),
                    cancellationToken)
                .ConfigureAwait(false);
            var contributions =
                module.GetUiContributions()?.ToArray() ??
                throw new InvalidDataException(
                    "Plugin UI contribution collection was null.");
            ValidateContributions(manifest, contributions);
            runtime.Contributions = contributions;
            entry.Runtime = runtime;
            runtime = null;
            entry.Descriptor = ToDescriptor(
                manifest,
                entry.Directory,
                PluginLoadState.Loaded,
                isEnabled: true,
                contributionCount: contributions.Length);
            _ = logger.Log(
                PluginLogLevel.Information,
                $"Plugin {manifest.Name} {manifest.Version} loaded.");
        }
        catch (Exception exception)
        {
            if (runtime is not null)
            {
                await runtime.DisposeAsync(CancellationToken.None)
                    .ConfigureAwait(false);
            }

            loadContext?.Unload();
            _ = logger.Log(
                PluginLogLevel.Error,
                "Plugin load failed and was isolated.",
                exception);
            entry.Runtime = null;
            entry.Descriptor = ToDescriptor(
                manifest,
                entry.Directory,
                PluginLoadState.Failed,
                isEnabled: true,
                errorMessage: exception.Message);
        }
    }

    private async Task UnloadEntryAsync(
        PluginEntry entry,
        CancellationToken cancellationToken)
    {
        var runtime = entry.Runtime;
        entry.Runtime = null;

        if (runtime is not null)
        {
            try
            {
                await runtime.DisposeAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                var logger = CreateLogger(
                    entry.Manifest?.Id ?? entry.Key);
                _ = logger.Log(
                    PluginLogLevel.Error,
                    "Plugin shutdown failure was isolated.",
                    exception);
            }
        }
    }

    private async Task UnloadAllAsync(
        CancellationToken cancellationToken)
    {
        foreach (var entry in _entries.Values)
        {
            await UnloadEntryAsync(entry, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<Result<Dictionary<string, bool>>>
        LoadActivationStateAsync(
            CancellationToken cancellationToken)
    {
        var path = Path.Combine(
            PluginsDirectory,
            StateFileName);

        if (!File.Exists(path))
        {
            return Result<Dictionary<string, bool>>.Success(
                new Dictionary<string, bool>(
                    StringComparer.OrdinalIgnoreCase));
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var state = await JsonSerializer
                .DeserializeAsync<PluginActivationDocument>(
                    stream,
                    JsonOptions,
                    cancellationToken)
                .ConfigureAwait(false);

            if (state is null || state.SchemaVersion != 1)
            {
                throw new InvalidDataException(
                    "Plugin activation state is invalid.");
            }

            return Result<Dictionary<string, bool>>.Success(
                new Dictionary<string, bool>(
                    state.Plugins,
                    StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception exception) when (
            exception is JsonException or
            InvalidDataException)
        {
            var corruptPath = path +
                $".corrupt.{_timeProvider.GetUtcNow():yyyyMMddHHmmssfff}";
            File.Move(path, corruptPath, overwrite: false);
            return Result<Dictionary<string, bool>>.Success(
                new Dictionary<string, bool>(
                    StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception exception)
        {
            return Failure<Dictionary<string, bool>>(
                exception,
                "Plugin activation state could not be loaded.",
                cancellationToken);
        }
    }

    private async Task<Result> SaveActivationStateAsync(
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(
            PluginsDirectory,
            StateFileName);
        var temporaryPath =
            $"{path}.tmp-{Guid.NewGuid():N}";

        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 16 * 1024,
                FileOptions.Asynchronous |
                FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                        stream,
                        new PluginActivationDocument(
                            1,
                            new Dictionary<string, bool>(
                                _activation,
                                StringComparer.OrdinalIgnoreCase)),
                        JsonOptions,
                        cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            File.Move(
                temporaryPath,
                path,
                overwrite: true);
            return Result.Success();
        }
        catch (Exception exception)
        {
            return Failure(
                exception,
                "Plugin activation state could not be saved.",
                cancellationToken);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private PluginManagerSnapshot PublishSnapshot()
    {
        var descriptors = _entries.Values
            .Select(entry => entry.Descriptor)
            .OrderBy(descriptor => descriptor.Name)
            .ThenBy(descriptor => descriptor.Id)
            .ToArray();
        var snapshot = new PluginManagerSnapshot(
            Array.AsReadOnly(descriptors),
            descriptors.Count(descriptor =>
                descriptor.State == PluginLoadState.Loaded),
            descriptors.Count(descriptor =>
                descriptor.State == PluginLoadState.Disabled),
            descriptors.Count(descriptor =>
                descriptor.State == PluginLoadState.Failed),
            descriptors.Count(descriptor =>
                descriptor.State ==
                PluginLoadState.Incompatible),
            descriptors.Sum(descriptor =>
                descriptor.UiContributionCount));

        lock (_sync)
        {
            _snapshot = snapshot;
        }

        return snapshot;
    }

    private static PluginDescriptor ToDescriptor(
        PluginManifest manifest,
        string directory,
        PluginLoadState state,
        bool isEnabled,
        string? errorMessage = null,
        int contributionCount = 0)
    {
        return new PluginDescriptor(
            manifest.Id,
            manifest.Name,
            manifest.Version,
            manifest.Capabilities.ToArray(),
            state,
            isEnabled,
            state == PluginLoadState.Loaded,
            contributionCount,
            directory,
            manifest.Description,
            manifest.Author,
            errorMessage);
    }

    private PluginFileLogger CreateLogger(string pluginId)
    {
        return new PluginFileLogger(
            _pluginLogsDirectory,
            pluginId,
            _timeProvider);
    }

    private static void ValidateContributions(
        PluginManifest manifest,
        IReadOnlyList<IPluginUiContribution> contributions)
    {
        if (contributions.Any(contribution =>
                contribution is null ||
                string.IsNullOrWhiteSpace(contribution.Id) ||
                string.IsNullOrWhiteSpace(contribution.Title) ||
                !manifest.Capabilities.Contains(
                    contribution.Kind)) ||
            contributions
                .Select(contribution => contribution.Id)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() != contributions.Count)
        {
            throw new InvalidDataException(
                "Plugin UI contributions are invalid or do not match " +
                "the manifest capabilities.");
        }
    }

    private async Task<Result> EnterAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await _gate.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            return Result.Success();
        }
        catch (OperationCanceledException exception)
        {
            return Result.Failure(
                new Error(
                    ErrorCode.Cancelled,
                    "Plugin operation was cancelled.",
                    exception));
        }
    }

    private static Result<T> Validation<T>(string message)
    {
        return Result<T>.Failure(
            new Error(ErrorCode.Validation, message));
    }

    private static Result<T> Failure<T>(
        Exception exception,
        string message,
        CancellationToken cancellationToken)
    {
        return exception switch
        {
            OperationCanceledException
                when cancellationToken.IsCancellationRequested =>
                Result<T>.Failure(
                    new Error(
                        ErrorCode.Cancelled,
                        message,
                        exception)),
            IOException or
            UnauthorizedAccessException or
            NotSupportedException =>
                Result<T>.Failure(
                    new Error(
                        ErrorCode.Io,
                        message,
                        exception)),
            JsonException or
            InvalidDataException or
            TypeLoadException or
            FileLoadException or
            BadImageFormatException or
            TargetInvocationException =>
                Result<T>.Failure(
                    new Error(
                        ErrorCode.Serialization,
                        message,
                        exception)),
            _ => Result<T>.Failure(
                new Error(
                    ErrorCode.Unexpected,
                    message,
                    exception)),
        };
    }

    private static Result Failure(
        Exception exception,
        string message,
        CancellationToken cancellationToken)
    {
        var result = Failure<object>(
            exception,
            message,
            cancellationToken);
        return Result.Failure(result.Error);
    }

    private sealed record DiscoveredPlugin(
        string Directory,
        PluginManifest? Manifest,
        string? ErrorMessage,
        PluginLoadState State);

    private sealed class PluginEntry(
        string key,
        string directory,
        PluginManifest? manifest,
        PluginDescriptor descriptor)
    {
        public string Key { get; } = key;

        public string Directory { get; } = directory;

        public PluginManifest? Manifest { get; } = manifest;

        public PluginDescriptor Descriptor { get; set; } =
            descriptor;

        public PluginRuntime? Runtime { get; set; }
    }

    private sealed class PluginContext(
        string pluginId,
        string pluginDirectory,
        Version hostVersion,
        IPluginLogger logger) : IPluginContext
    {
        private IServiceProvider? _services;

        public string PluginId { get; } = pluginId;

        public string PluginDirectory { get; } =
            pluginDirectory;

        public Version ApiVersion => PluginApiVersion.Current;

        public Version HostVersion { get; } = hostVersion;

        public IServiceProvider Services =>
            _services ??
            throw new InvalidOperationException(
                "Plugin services are not initialized.");

        public IPluginLogger Logger { get; } = logger;

        public void SetServices(IServiceProvider services)
        {
            _services = services;
        }
    }

    private sealed class PluginRuntime(
        PluginLoadContext loadContext,
        IMemoryInspectorPlugin module,
        ServiceProvider serviceProvider,
        IReadOnlyList<IPluginUiContribution> contributions)
    {
        public PluginLoadContext LoadContext { get; } =
            loadContext;

        public IMemoryInspectorPlugin Module { get; } = module;

        public ServiceProvider ServiceProvider { get; } =
            serviceProvider;

        public IReadOnlyList<IPluginUiContribution>
            Contributions { get; set; } = contributions;

        public async Task DisposeAsync(
            CancellationToken cancellationToken)
        {
            try
            {
                await Module.ShutdownAsync(cancellationToken)
                    .AsTask()
                    .WaitAsync(
                        TimeSpan.FromSeconds(5),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
            }

            foreach (var contribution in Contributions)
            {
                try
                {
                    if (contribution is
                        IAsyncDisposable asyncDisposable)
                    {
                        await asyncDisposable.DisposeAsync()
                            .AsTask()
                            .WaitAsync(
                                TimeSpan.FromSeconds(5),
                                cancellationToken)
                            .ConfigureAwait(false);
                    }
                    else if (contribution is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }
                catch
                {
                }
            }

            try
            {
                await ServiceProvider.DisposeAsync()
                    .AsTask()
                    .WaitAsync(
                        TimeSpan.FromSeconds(5),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
            }

            try
            {
                if (Module is
                    IAsyncDisposable moduleAsyncDisposable)
                {
                    await moduleAsyncDisposable.DisposeAsync()
                        .AsTask()
                        .WaitAsync(
                            TimeSpan.FromSeconds(5),
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                else if (Module is IDisposable moduleDisposable)
                {
                    moduleDisposable.Dispose();
                }
            }
            catch
            {
            }

            Contributions = [];
            LoadContext.Unload();
        }
    }

    private sealed record PluginActivationDocument(
        int SchemaVersion,
        Dictionary<string, bool> Plugins);
}
