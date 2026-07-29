using MemoryInspector.Common;
using MemoryInspector.Core.Memory;

namespace MemoryInspector.Application.Memory;

public sealed class MemoryRegionQueryResult
{
    public MemoryRegionQueryResult(
        IEnumerable<MemoryRegion> regions,
        IEnumerable<Error>? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(regions);

        Regions = Array.AsReadOnly(regions.ToArray());
        Warnings = Array.AsReadOnly(
            warnings?.ToArray() ?? Array.Empty<Error>());

        if (Warnings.Any(error => error.Code == ErrorCode.None))
        {
            throw new ArgumentException(
                "A query warning must describe a failure.",
                nameof(warnings));
        }
    }

    public IReadOnlyList<MemoryRegion> Regions { get; }

    public IReadOnlyList<Error> Warnings { get; }

    public bool IsPartial => Warnings.Count > 0;
}
