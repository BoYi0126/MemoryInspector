using MemoryInspector.Common;
using MemoryInspector.Core.Monitoring;

namespace MemoryInspector.Windows.Memory;

internal interface IProcessIdentityValidator
{
    Result Validate(MonitoringSessionIdentity identity);
}
