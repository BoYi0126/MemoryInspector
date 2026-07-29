using MemoryInspector.Common;

namespace MemoryInspector.Application.Configuration;

public interface ISettingsService
{
    Task<Result<AppSettings>> LoadAsync(
        CancellationToken cancellationToken = default);

    Task<Result> SaveAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default);
}
