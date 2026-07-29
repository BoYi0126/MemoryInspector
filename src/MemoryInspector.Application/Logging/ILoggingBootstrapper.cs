using MemoryInspector.Common;

namespace MemoryInspector.Application.Logging;

public interface ILoggingBootstrapper
{
    Result<IAppLogger> Initialize();
}
