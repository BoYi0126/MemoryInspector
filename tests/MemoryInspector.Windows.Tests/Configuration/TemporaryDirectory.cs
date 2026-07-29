namespace MemoryInspector.Windows.Tests.Configuration;

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        RootPath = Path.Combine(
            Path.GetTempPath(),
            "MemoryInspector.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(RootPath);
    }

    public string RootPath { get; }

    public void Dispose()
    {
        if (Directory.Exists(RootPath))
        {
            Directory.Delete(RootPath, recursive: true);
        }
    }
}
