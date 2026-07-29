using MemoryInspector.Application.Configuration;
using MemoryInspector.Application.Logging;
using MemoryInspector.Common;

namespace MemoryInspector.Windows.Logging;

public sealed class FileLoggingBootstrapper : ILoggingBootstrapper
{
    private readonly IAppPathService _pathService;
    private readonly TimeProvider _timeProvider;

    public FileLoggingBootstrapper(
        IAppPathService pathService,
        TimeProvider timeProvider)
    {
        _pathService = Guard.NotNull(pathService);
        _timeProvider = Guard.NotNull(timeProvider);
    }

    public Result<IAppLogger> Initialize()
    {
        var directoryResult = _pathService.EnsureDirectories();

        if (directoryResult.IsFailure)
        {
            return Result<IAppLogger>.Failure(directoryResult.Error);
        }

        IAppLogger logger = new DailyFileLogger(_pathService, _timeProvider);
        var logResult = logger.Log(
            AppLogLevel.Information,
            "MemoryInspector logging initialized.");

        return logResult.IsSuccess
            ? Result<IAppLogger>.Success(logger)
            : Result<IAppLogger>.Failure(logResult.Error);
    }
}
