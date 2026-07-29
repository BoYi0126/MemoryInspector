using Microsoft.Extensions.DependencyInjection;
using MemoryInspector.Application.Configuration;
using MemoryInspector.Application.Logging;
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

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();

        await _serviceProvider
            .GetRequiredService<ProcessExplorerViewModel>()
            .InitializeAsync(settingsResult.Value);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
