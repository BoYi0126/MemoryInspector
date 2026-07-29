using MemoryInspector.Common;

namespace MemoryInspector.Application.Scanning.History;

public interface IScanHistoryStore
{
    Task<Result<ScanHistoryDocument>> LoadAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    Task<Result> SaveAsync(
        ScanHistoryDocument document,
        CancellationToken cancellationToken = default);
}
