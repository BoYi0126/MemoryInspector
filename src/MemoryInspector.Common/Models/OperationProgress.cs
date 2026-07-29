namespace MemoryInspector.Common;

/// <summary>
/// Reports completed work for determinate or indeterminate operations.
/// </summary>
public sealed record OperationProgress
{
    public OperationProgress(
        long completed,
        long? total = null,
        string? stage = null)
    {
        Guard.NonNegative(completed);

        if (total is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(total),
                total,
                "Total cannot be negative.");
        }

        if (total.HasValue && completed > total.Value)
        {
            throw new ArgumentOutOfRangeException(
                nameof(completed),
                completed,
                "Completed work cannot exceed total work.");
        }

        Completed = completed;
        Total = total;
        Stage = stage;
    }

    public long Completed { get; }

    public long? Total { get; }

    public string? Stage { get; }

    public bool IsIndeterminate => !Total.HasValue;

    public double? Percentage => Total switch
    {
        null => null,
        0 => 100d,
        _ => Completed * 100d / Total.Value,
    };
}
