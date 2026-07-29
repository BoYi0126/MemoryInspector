using MemoryInspector.Common;
using MemoryInspector.Core.Processes;

namespace MemoryInspector.Application.SavedAddresses;

public sealed record SavedAddressTarget
{
    public SavedAddressTarget(
        string processName,
        ProcessArchitecture architecture)
    {
        if (architecture == ProcessArchitecture.Unknown)
        {
            throw new ArgumentException(
                "A saved-address target requires a known architecture.",
                nameof(architecture));
        }

        ProcessName = Guard.NotNullOrWhiteSpace(processName).Trim();
        Architecture = architecture;
    }

    public string ProcessName { get; }

    public ProcessArchitecture Architecture { get; }
}
