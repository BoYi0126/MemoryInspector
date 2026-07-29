using MemoryInspector.Application.Logging;
using MemoryInspector.Common;

namespace MemoryInspector.Windows.Tests.Configuration;

internal sealed class RecordingLogger : IAppLogger
{
    public List<LogEntry> Entries { get; } = [];

    public Result Log(
        AppLogLevel level,
        string message,
        Exception? exception = null)
    {
        Entries.Add(new LogEntry(level, message, exception));
        return Result.Success();
    }
}

internal sealed record LogEntry(
    AppLogLevel Level,
    string Message,
    Exception? Exception);
