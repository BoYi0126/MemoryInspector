using MemoryInspector.Common;
using MemoryInspector.Core.Scanning;

namespace MemoryInspector.Core.Memory.Editing;

public interface IMemoryValueSerializer
{
    Result<MemoryValueSerialization> Serialize(
        string input,
        ScanValueType valueType,
        MemoryFloatingPointPolicy floatingPointPolicy =
            MemoryFloatingPointPolicy.RejectNonFinite);
}
