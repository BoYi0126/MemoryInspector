namespace MemoryInspector.Core.Memory.Editing;

public enum MemoryWriteFailureReason
{
    None = 0,
    FeatureDisabled = 1,
    SessionInvalid = 2,
    TargetExited = 3,
    ManualAddressDisabled = 4,
    InvalidRequest = 5,
    AccessDenied = 6,
    InvalidAddress = 7,
    RegionNotFound = 8,
    RegionNotCommitted = 9,
    RegionNotWritable = 10,
    GuardPage = 11,
    RangeOverflow = 12,
    OriginalReadFailed = 13,
    OriginalValueMismatch = 14,
    WriterDenied = 15,
    PartialWrite = 16,
    WriteFailed = 17,
    VerificationReadFailed = 18,
    VerificationMismatch = 19,
    AuditFailed = 20,
    Cancelled = 21,
    Unknown = 255,
}
