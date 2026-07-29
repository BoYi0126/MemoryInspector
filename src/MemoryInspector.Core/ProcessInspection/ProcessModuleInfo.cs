using MemoryInspector.Common;

namespace MemoryInspector.Core.ProcessInspection;

public sealed record ProcessModuleInfo
{
    public ProcessModuleInfo(
        string name,
        ulong? baseAddress,
        ulong? size,
        string? path,
        string? version,
        IReadOnlyList<Error>? warnings = null)
    {
        Name = Guard.NotNullOrWhiteSpace(name);
        BaseAddress = baseAddress;
        Size = size;
        Path = string.IsNullOrWhiteSpace(path)
            ? null
            : path.Trim();
        Version = string.IsNullOrWhiteSpace(version)
            ? null
            : version.Trim();
        Warnings = Array.AsReadOnly(
            warnings?.ToArray() ?? []);
    }

    public string Name { get; }

    public ulong? BaseAddress { get; }

    public ulong? Size { get; }

    public string? Path { get; }

    public string? Version { get; }

    public IReadOnlyList<Error> Warnings { get; }

    public bool IsPartial => Warnings.Count > 0;
}
