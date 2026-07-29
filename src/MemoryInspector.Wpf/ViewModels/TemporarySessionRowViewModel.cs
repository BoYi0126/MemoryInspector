using MemoryInspector.Application.Temporary;

namespace MemoryInspector.Wpf.ViewModels;

public sealed class TemporarySessionRowViewModel(
    TemporarySessionInfo session)
{
    public TemporarySessionInfo Session { get; } =
        session ?? throw new ArgumentNullException(nameof(session));

    public Guid SessionId => Session.SessionId;

    public string SessionDisplay =>
        Session.IsCurrent
            ? $"{Session.SessionId:D} (current)"
            : Session.SessionId.ToString("D");

    public string SizeDisplay =>
        TemporaryManagerViewModel.FormatBytes(Session.TotalBytes);

    public string StateDisplay =>
        Session.HasReadableHistory
            ? Session.PinnedNodeCount > 0
                ? $"{Session.PinnedNodeCount:N0} pinned"
                : "Ready"
            : "History unavailable";
}
