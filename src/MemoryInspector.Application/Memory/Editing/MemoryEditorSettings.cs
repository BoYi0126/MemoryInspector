using MemoryInspector.Common;

namespace MemoryInspector.Application.Memory.Editing;

public sealed record MemoryEditorSettings
{
    public bool Enabled { get; init; }

    public bool RequireConfirmation { get; init; } = true;

    public bool VerifyAfterWrite { get; init; } = true;

    public bool AllowManualAddress { get; init; }

    public DateTimeOffset? EnabledAt { get; init; }

    public static MemoryEditorSettings CreateDefault() => new();

    public Result Validate()
    {
        if (Enabled != EnabledAt.HasValue)
        {
            return Result.Failure(
                new Error(
                    ErrorCode.Validation,
                    "Memory Editor enabled state and enabled time " +
                    "must be set together."));
        }

        return Result.Success();
    }
}
