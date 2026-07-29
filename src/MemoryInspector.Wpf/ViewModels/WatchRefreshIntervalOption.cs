namespace MemoryInspector.Wpf.ViewModels;

public sealed record WatchRefreshIntervalOption(
    string Label,
    int? Milliseconds)
{
    public static IReadOnlyList<WatchRefreshIntervalOption>
        Defaults { get; } =
        Array.AsReadOnly<WatchRefreshIntervalOption>(
        [
            new("250 ms", 250),
            new("500 ms", 500),
            new("1000 ms", 1_000),
            new("Custom", null),
        ]);

    public bool IsCustom => !Milliseconds.HasValue;
}
