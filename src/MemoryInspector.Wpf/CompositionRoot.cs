using Microsoft.Extensions.DependencyInjection;
using MemoryInspector.Application.Configuration;
using MemoryInspector.Application.Logging;
using MemoryInspector.Application.Memory;
using MemoryInspector.Application.Memory.Editing;
using MemoryInspector.Application.Monitoring;
using MemoryInspector.Application.Processes;
using MemoryInspector.Application.ProcessInspection;
using MemoryInspector.Application.SavedAddresses;
using MemoryInspector.Application.Scanning;
using MemoryInspector.Application.Scanning.History;
using MemoryInspector.Application.Scanning.Results;
using MemoryInspector.Application.Scanning.Snapshots;
using MemoryInspector.Application.Scanning.Snapshots.Comparison;
using MemoryInspector.Application.Temporary;
using MemoryInspector.Application.Watch;
using MemoryInspector.Core.Scanning;
using MemoryInspector.Core.Memory.Editing;
using MemoryInspector.Windows.Configuration;
using MemoryInspector.Windows.Logging;
using MemoryInspector.Windows.Memory;
using MemoryInspector.Windows.Memory.Editing;
using MemoryInspector.Windows.Monitoring;
using MemoryInspector.Windows.Processes;
using MemoryInspector.Windows.ProcessInspection;
using MemoryInspector.Windows.SavedAddresses;
using MemoryInspector.Windows.Scanning.History;
using MemoryInspector.Windows.Scanning.Snapshots;
using MemoryInspector.Windows.Temporary;
using MemoryInspector.Plugin;
using MemoryInspector.Plugin.Runtime;
using MemoryInspector.Wpf.ViewModels;
using MemoryInspector.Wpf.Services;
using System.IO;

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
        services.AddSingleton<
            IMonitoringTargetConnectionFactory,
            WindowsMonitoringTargetConnectionFactory>();
        services.AddSingleton<IMonitoringSessionService>(serviceProvider =>
            new MonitoringSessionService(
                serviceProvider.GetRequiredService<
                    IMonitoringTargetConnectionFactory>(),
                serviceProvider.GetRequiredService<IAppLogger>(),
                serviceProvider.GetRequiredService<TimeProvider>(),
                TimeSpan.FromSeconds(1)));
        services.AddSingleton<
            IMemoryRegionProvider,
            WindowsMemoryRegionProvider>();
        services.AddSingleton<IMemoryRegionService, MemoryRegionService>();
        services.AddSingleton<WindowsProcessDetailsProvider>();
        services.AddSingleton<IProcessModuleProvider>(
            serviceProvider =>
                serviceProvider.GetRequiredService<
                    WindowsProcessDetailsProvider>());
        services.AddSingleton<IProcessThreadProvider>(
            serviceProvider =>
                serviceProvider.GetRequiredService<
                    WindowsProcessDetailsProvider>());
        services.AddSingleton<
            IProcessModuleService,
            ProcessModuleService>();
        services.AddSingleton<
            IProcessThreadService,
            ProcessThreadService>();
        services.AddSingleton<
            IMemoryReaderProvider,
            WindowsMemoryReaderProvider>();
        services.AddSingleton<IMemoryReaderService, MemoryReaderService>();
        services.AddSingleton<IScanValueParser, InvariantScanValueParser>();
        services.AddSingleton<IMemoryValueSerializer>(
            serviceProvider =>
                new MemoryValueSerializer(
                    serviceProvider.GetRequiredService<
                        IScanValueParser>(),
                    MemoryByteOrder.LittleEndian));
        services.AddSingleton<IValueMatcher, DefaultValueMatcher>();
        services.AddSingleton<IFirstScanService, ExactValueFirstScanService>();
        services.AddSingleton<
            IExactInitialSnapshotService,
            ExactInitialSnapshotService>();
        services.AddSingleton<
            IUnknownInitialScanService,
            UnknownInitialScanService>();
        services.AddSingleton<INextScanService, NextScanService>();
        services.AddSingleton<
            ISnapshotNodeIdAllocator,
            SnapshotNodeIdAllocator>();
        services.AddSingleton<
            IDurationFilterService,
            DurationFilterService>();
        services.AddSingleton<
            IFilterPipelineService,
            FilterPipelineService>();
        services.AddSingleton<
            IScanWorkflowService,
            ScanWorkflowService>();
        services.AddSingleton<
            IScanHistoryStore,
            JsonScanHistoryStore>();
        services.AddSingleton<BinarySnapshotStorage>();
        services.AddSingleton<LruSnapshotStorage>(serviceProvider =>
            new LruSnapshotStorage(
                serviceProvider.GetRequiredService<
                    BinarySnapshotStorage>(),
                serviceProvider.GetRequiredService<
                    ISettingsService>(),
                serviceProvider.GetRequiredService<
                    IAppPathService>(),
                serviceProvider.GetRequiredService<
                    TimeProvider>()));
        services.AddSingleton<ISnapshotStorage>(serviceProvider =>
            serviceProvider.GetRequiredService<
                LruSnapshotStorage>());
        services.AddSingleton<
            ISnapshotCompareService,
            SnapshotCompareService>();
        services.AddSingleton<
            ISnapshotComparisonExportService,
            CsvSnapshotComparisonExportService>();
        services.AddSingleton<ISnapshotCacheManager>(
            serviceProvider =>
                serviceProvider.GetRequiredService<
                    LruSnapshotStorage>());
        services.AddSingleton<
            ITemporaryManagerService,
            WindowsTemporaryManagerService>();
        services.AddSingleton<IPluginManager>(serviceProvider =>
        {
            var paths = serviceProvider
                .GetRequiredService<IAppPathService>();
            return new PluginManager(
                paths.PluginsDirectory,
                Path.Combine(paths.LogsDirectory, "Plugins"),
                PluginApiVersion.HostVersion,
                serviceProvider.GetRequiredService<TimeProvider>());
        });
        services.AddSingleton<IResultGridService, ResultGridService>();
        services.AddSingleton<IWatchService, WatchService>();
        services.AddSingleton<
            IMemoryEditorFeatureService,
            MemoryEditorFeatureService>();
        services.AddSingleton<
            IMemoryWriteAuditService,
            JsonMemoryWriteAuditService>();
        services.AddSingleton<
            IMemoryWriteAuditExportService,
            CsvMemoryWriteAuditExportService>();
        services.AddSingleton<IMemoryWriter, WindowsMemoryWriter>();
        services.AddSingleton<
            IMemoryWriteService,
            MemoryWriteService>();
        services.AddSingleton<
            ISavedAddressStore,
            JsonSavedAddressStore>();
        services.AddSingleton<
            ISavedAddressService,
            SavedAddressService>();
        services.AddSingleton<IClipboardService, WpfClipboardService>();
        services.AddSingleton<
            IUserConfirmationService,
            WpfUserConfirmationService>();
        services.AddSingleton<
            IJsonFileDialogService,
            WpfJsonFileDialogService>();
        services.AddSingleton<
            IMemoryEditorFileDialogService,
            WpfMemoryEditorFileDialogService>();
        services.AddSingleton<
            ISnapshotCompareFileDialogService,
            WpfSnapshotCompareFileDialogService>();
        services.AddSingleton<ProcessExplorerViewModel>();
        services.AddSingleton<MemoryRegionViewerViewModel>();
        services.AddSingleton<ScanWorkspaceViewModel>();
        services.AddSingleton<ProcessDetailsViewerViewModel>();
        services.AddSingleton<HexViewerViewModel>();
        services.AddSingleton<SnapshotCompareViewModel>();
        services.AddSingleton<ResultGridViewModel>();
        services.AddSingleton<WatchWindowViewModel>();
        services.AddSingleton<SavedAddressWindowViewModel>();
        services.AddSingleton<MemoryEditorViewModel>();
        services.AddSingleton<TemporaryManagerViewModel>();
        services.AddSingleton<PluginManagerViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            });
    }
}
