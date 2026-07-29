namespace MemoryInspector.Core.Memory.Editing;

public enum MemoryWriteVerificationStatus
{
    NotRequested = 0,
    Verified = 1,
    Mismatch = 2,
    ReadFailed = 3,
}
