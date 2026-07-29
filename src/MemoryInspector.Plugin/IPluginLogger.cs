using MemoryInspector.Common;

namespace MemoryInspector.Plugin;

public interface IPluginLogger
{
    Result Log(
        PluginLogLevel level,
        string message,
        Exception? exception = null);
}
