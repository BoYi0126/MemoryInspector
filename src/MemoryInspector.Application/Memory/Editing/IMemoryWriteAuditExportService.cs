using MemoryInspector.Common;
using MemoryInspector.Core.Memory.Editing;

namespace MemoryInspector.Application.Memory.Editing;

public interface IMemoryWriteAuditExportService
{
    Task<Result> ExportSummaryAsync(
        string path,
        IReadOnlyList<MemoryWriteAuditEntry> entries,
        CancellationToken cancellationToken = default);
}
