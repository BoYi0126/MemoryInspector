using MemoryInspector.Application.Configuration;
using MemoryInspector.Common;

namespace MemoryInspector.Windows.Configuration;

public sealed class AppPathService : IAppPathService
{
    private static readonly string[] DirectoryNames =
    [
        "Config",
        "Temp",
        "Sessions",
        "SavedAddresses",
        "Plugins",
        "Logs",
        "Audit",
    ];

    public AppPathService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MemoryInspector"))
    {
    }

    public AppPathService(string rootDirectory)
    {
        Guard.NotNullOrWhiteSpace(rootDirectory);

        RootDirectory = Path.GetFullPath(rootDirectory);
        ConfigDirectory = Path.Combine(RootDirectory, DirectoryNames[0]);
        SettingsFilePath = Path.Combine(ConfigDirectory, "settings.json");
        TempDirectory = Path.Combine(RootDirectory, DirectoryNames[1]);
        SessionsDirectory = Path.Combine(RootDirectory, DirectoryNames[2]);
        SavedAddressesDirectory = Path.Combine(RootDirectory, DirectoryNames[3]);
        PluginsDirectory = Path.Combine(RootDirectory, DirectoryNames[4]);
        LogsDirectory = Path.Combine(RootDirectory, DirectoryNames[5]);
        AuditDirectory = Path.Combine(RootDirectory, DirectoryNames[6]);
        MemoryEditorAuditDirectory = Path.Combine(
            AuditDirectory,
            "MemoryEditor");
    }

    public string RootDirectory { get; }

    public string ConfigDirectory { get; }

    public string SettingsFilePath { get; }

    public string TempDirectory { get; }

    public string SessionsDirectory { get; }

    public string SavedAddressesDirectory { get; }

    public string PluginsDirectory { get; }

    public string LogsDirectory { get; }

    public string AuditDirectory { get; }

    public string MemoryEditorAuditDirectory { get; }

    public Result EnsureDirectories()
    {
        try
        {
            Directory.CreateDirectory(RootDirectory);

            foreach (var directoryName in DirectoryNames)
            {
                Directory.CreateDirectory(
                    Path.Combine(RootDirectory, directoryName));
            }

            Directory.CreateDirectory(MemoryEditorAuditDirectory);

            return Result.Success();
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            NotSupportedException)
        {
            return Result.Failure(
                new Error(
                    ErrorCode.Io,
                    "MemoryInspector data directories could not be created.",
                    exception));
        }
    }
}
