using MemoryInspector.Common;

namespace MemoryInspector.Application.Configuration;

public interface IAppPathService
{
    string RootDirectory { get; }

    string ConfigDirectory { get; }

    string SettingsFilePath { get; }

    string TempDirectory { get; }

    string SessionsDirectory { get; }

    string SavedAddressesDirectory { get; }

    string PluginsDirectory { get; }

    string LogsDirectory { get; }

    Result EnsureDirectories();
}
