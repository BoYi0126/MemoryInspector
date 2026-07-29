using MemoryInspector.Common;
using MemoryInspector.Core.ProcessInspection;

namespace MemoryInspector.Application.ProcessInspection;

public sealed record ProcessModuleQueryResult
{
    public ProcessModuleQueryResult(
        IReadOnlyList<ProcessModuleInfo> modules,
        IReadOnlyList<Error>? warnings = null)
    {
        Modules = Array.AsReadOnly(
            modules?.ToArray() ??
            throw new ArgumentNullException(nameof(modules)));
        Warnings = Array.AsReadOnly(
            warnings?.ToArray() ?? []);
    }

    public IReadOnlyList<ProcessModuleInfo> Modules { get; }

    public IReadOnlyList<Error> Warnings { get; }

    public bool IsPartial =>
        Warnings.Count > 0 ||
        Modules.Any(module => module.IsPartial);
}
