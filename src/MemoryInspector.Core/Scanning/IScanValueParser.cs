using MemoryInspector.Common;

namespace MemoryInspector.Core.Scanning;

public interface IScanValueParser
{
    Result<ScanValue> Parse(
        string input,
        ScanValueType valueType);
}
