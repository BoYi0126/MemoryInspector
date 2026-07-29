using MemoryInspector.Common;

namespace MemoryInspector.Application.Logging;

public interface IAppLogger
{
    Result Log(
        AppLogLevel level,
        string message,
        Exception? exception = null);
}
