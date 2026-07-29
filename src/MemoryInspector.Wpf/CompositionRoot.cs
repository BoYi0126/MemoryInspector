using Microsoft.Extensions.DependencyInjection;
using MemoryInspector.Application.Configuration;
using MemoryInspector.Application.Logging;
using MemoryInspector.Application.Processes;
using MemoryInspector.Windows.Configuration;
using MemoryInspector.Windows.Logging;
using MemoryInspector.Windows.Processes;
using MemoryInspector.Wpf.ViewModels;

namespace MemoryInspector.Wpf;

internal static class CompositionRoot
{
    public static ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();

        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<IAppPathService, AppPathService>();
        services.AddSingleton<ILoggingBootstrapper, FileLoggingBootstrapper>();
        services.AddSingleton<IAppLogger>(serviceProvider =>
        {
            var result = serviceProvider
                .GetRequiredService<ILoggingBootstrapper>()
                .Initialize();

            if (result.IsFailure)
            {
                throw new InvalidOperationException(
                    result.Error.ToDisplayMessage(),
                    result.Error.Exception);
            }

            return result.Value;
        });
        services.AddSingleton<ISettingsService, JsonSettingsService>();
        services.AddSingleton<ISystemProcessService, SystemProcessService>();
        services.AddSingleton<ProcessExplorerViewModel>();
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            });
    }
}
