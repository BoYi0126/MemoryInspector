namespace MemoryInspector.Application.Scanning;

[Flags]
public enum DurationObservationFlags
{
    None = 0,
    HasChanged = 1,
    HasIncreased = 2,
    HasDecreased = 4,
    ReadFailed = 8,
}
