using System.Globalization;
using System.Text;
using MemoryInspector.Application.Configuration;
using MemoryInspector.Application.Logging;
using MemoryInspector.Common;

namespace MemoryInspector.Windows.Logging;

internal sealed class DailyFileLogger : IAppLogger
{
    private readonly object _writeLock = new();
    private readonly IAppPathService _pathService;
    private readonly TimeProvider _timeProvider;

    public DailyFileLogger(
        IAppPathService pathService,
        TimeProvider timeProvider)
    {
        _pathService = Guard.NotNull(pathService);
        _timeProvider = Guard.NotNull(timeProvider);
    }

    public Result Log(
        AppLogLevel level,
        string message,
        Exception? exception = null)
    {
        Guard.NotNullOrWhiteSpace(message);

        try
        {
            lock (_writeLock)
            {
                var timestamp = _timeProvider.GetLocalNow();
                var filePath = Path.Combine(
                    _pathService.LogsDirectory,
                    $"MemoryInspector-{timestamp:yyyyMMdd}.log");

                var line = BuildLogLine(timestamp, level, message, exception);
                File.AppendAllText(filePath, line, new UTF8Encoding(false));
            }

            return Result.Success();
        }
        catch (Exception loggingException) when (
            loggingException is IOException or
            UnauthorizedAccessException or
            NotSupportedException)
        {
            return Result.Failure(
                new Error(
                    ErrorCode.Io,
                    "The application log could not be written.",
                    loggingException));
        }
    }

    private static string BuildLogLine(
        DateTimeOffset timestamp,
        AppLogLevel level,
        string message,
        Exception? exception)
    {
        var builder = new StringBuilder()
            .Append(
                timestamp.ToString(
                    "yyyy-MM-ddTHH:mm:ss.fffzzz",
                    CultureInfo.InvariantCulture))
            .Append(" [")
            .Append(level.ToString().ToUpperInvariant())
            .Append("] ")
            .Append(message);

        if (exception is not null)
        {
            builder
                .Append(" | ")
                .Append(exception);
        }

        return builder.AppendLine().ToString();
    }
}
