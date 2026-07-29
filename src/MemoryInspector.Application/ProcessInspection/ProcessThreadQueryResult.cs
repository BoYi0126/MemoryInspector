using MemoryInspector.Common;
using MemoryInspector.Core.ProcessInspection;

namespace MemoryInspector.Application.ProcessInspection;

public sealed record ProcessThreadQueryResult
{
    public ProcessThreadQueryResult(
        IReadOnlyList<ProcessThreadInfo> threads,
        IReadOnlyList<Error>? warnings = null)
    {
        Threads = Array.AsReadOnly(
            threads?.ToArray() ??
            throw new ArgumentNullException(nameof(threads)));
        Warnings = Array.AsReadOnly(
            warnings?.ToArray() ?? []);
    }

    public IReadOnlyList<ProcessThreadInfo> Threads { get; }

    public IReadOnlyList<Error> Warnings { get; }

    public bool IsPartial =>
        Warnings.Count > 0 ||
        Threads.Any(thread => thread.IsPartial);
}
