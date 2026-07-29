using MemoryInspector.Common;
using MemoryInspector.Core.ProcessInspection;

namespace MemoryInspector.Wpf.ViewModels;

public sealed class ProcessModuleRowViewModel(
    ProcessModuleInfo module)
{
    public ProcessModuleInfo Module { get; } =
        module ?? throw new ArgumentNullException(nameof(module));

    public bool IsStale => false;

    public string Name => Module.Name;

    public ulong? BaseAddress => Module.BaseAddress;

    public string BaseAddressDisplay =>
        BaseAddress.HasValue
            ? $"0x{BaseAddress.Value:X16}"
            : "Unavailable";

    public ulong? Size => Module.Size;

    public string SizeDisplay =>
        Size.HasValue && Size.Value <= long.MaxValue
            ? ByteSizeFormatter.Format((long)Size.Value)
            : Size.HasValue
                ? $"{Size.Value:N0} bytes"
                : "Unavailable";

    public string PathDisplay =>
        Module.Path ?? "Unavailable";

    public string VersionDisplay =>
        Module.Version ?? "Unavailable";

    public string WarningDisplay => string.Join(
        " | ",
        Module.Warnings.Select(warning => warning.Message));

    public bool HasSameIdentity(
        ProcessModuleRowViewModel other)
    {
        return Name.Equals(
                   other.Name,
                   StringComparison.OrdinalIgnoreCase) &&
               BaseAddress == other.BaseAddress &&
               string.Equals(
                   Module.Path,
                   other.Module.Path,
                   StringComparison.OrdinalIgnoreCase);
    }
}
