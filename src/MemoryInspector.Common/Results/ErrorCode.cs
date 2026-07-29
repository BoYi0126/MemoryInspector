namespace MemoryInspector.Common;

/// <summary>
/// Identifies a stable error category that presentation layers can translate.
/// </summary>
public enum ErrorCode
{
    None = 0,
    Validation = 1,
    NotFound = 2,
    AccessDenied = 3,
    InvalidState = 4,
    Cancelled = 5,
    Timeout = 6,
    Io = 7,
    NativeApi = 8,
    Serialization = 9,
    ResourceExhausted = 10,
    Unexpected = 255,
}
