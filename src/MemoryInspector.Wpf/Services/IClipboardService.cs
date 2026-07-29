using MemoryInspector.Common;

namespace MemoryInspector.Wpf.Services;

public interface IClipboardService
{
    Result SetText(string text);
}
