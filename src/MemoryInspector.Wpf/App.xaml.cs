using Microsoft.Extensions.DependencyInjection;
using MemoryInspector.Application.Configuration;
using MemoryInspector.Application.Logging;
using MemoryInspector.Application.Memory.Editing;
using MemoryInspector.Application.Temporary;
using MemoryInspector.Plugin;
using MemoryInspector.Wpf.ViewModels;
using System.Windows;

namespace MemoryInspector.Wpf;

public partial class App : System.Windows.Application
{
    private ServiceProvider? _serviceProvider;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _serviceProvider = CompositionRoot.CreateServiceProvider();
        var logger = _serviceProvider.GetRequiredService<IAppLogger>();
        var settingsResult = await _serviceProvider
            .GetRequiredService<ISettingsService>()
            .LoadAsync();

        if (settingsResult.IsFailure)
        {
            _ = logger.Log(
                AppLogLevel.Critical,
                settingsResult.Error.ToDisplayMessage(),
                settingsResult.Error.Exception);

            throw new InvalidOperationException(
                settingsResult.Error.ToDisplayMessage(),
                settingsResult.Error.Exception);
        }

        var memoryEditorFeature = _serviceProvider
            .GetRequiredService<IMemoryEditorFeatureService>()
            .Initialize(settingsResult.Value);

        if (memoryEditorFeature.IsFailure)
        {
            _ = logger.Log(
                AppLogLevel.Critical,
                memoryEditorFeature.Error.ToDisplayMessage(),
                memoryEditorFeature.Error.Exception);
            throw new InvalidOperationException(
                memoryEditorFeature.Error.ToDisplayMessage(),
                memoryEditorFeature.Error.Exception);
        }

        var cleanupResult = await _serviceProvider
            .GetRequiredService<ITemporaryManagerService>()
            .RunAutomaticCleanupAsync();

        if (cleanupResult.IsFailure)
        {
            _ = logger.Log(
                AppLogLevel.Warning,
                cleanupResult.Error.ToDisplayMessage(),
                cleanupResult.Error.Exception);
        }

        var pluginResult = await _serviceProvider
            .GetRequiredService<IPluginManager>()
            .InitializeAsync();

        if (pluginResult.IsFailure)
        {
            _ = logger.Log(
                AppLogLevel.Warning,
                pluginResult.Error.ToDisplayMessage(),
                pluginResult.Error.Exception);
        }

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();

        await _serviceProvider
            .GetRequiredService<ProcessExplorerViewModel>()
            .InitializeAsync(settingsResult.Value);
        await _serviceProvider
            .GetRequiredService<ResultGridViewModel>()
            .InitializeAsync(settingsResult.Value);
        _serviceProvider
            .GetRequiredService<WatchWindowViewModel>()
            .Initialize(settingsResult.Value);
        await _serviceProvider
            .GetRequiredService<SavedAddressWindowViewModel>()
            .InitializeAsync();
        await _serviceProvider
            .GetRequiredService<MemoryEditorViewModel>()
            .InitializeAsync();
        await _serviceProvider
            .GetRequiredService<TemporaryManagerViewModel>()
            .InitializeAsync();
        await _serviceProvider
            .GetRequiredService<PluginManagerViewModel>()
            .InitializeAsync();
        await _serviceProvider
            .GetRequiredService<SnapshotCompareViewModel>()
            .InitializeAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?
            .DisposeAsync()
            .AsTask()
            .GetAwaiter()
            .GetResult();
        base.OnExit(e);
    }
}
