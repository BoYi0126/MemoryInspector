using MemoryInspector.Application.Configuration;
using MemoryInspector.Common;

namespace MemoryInspector.Application.Memory.Editing;

public interface IMemoryEditorFeatureService
{
    MemoryEditorFeatureState State { get; }

    event EventHandler<MemoryEditorFeatureChangedEventArgs>?
        StateChanged;

    Result<MemoryEditorFeatureState> Initialize(
        AppSettings settings);

    Task<Result<MemoryEditorFeatureState>> EnableAsync(
        MemoryEditorEnablementAcknowledgement acknowledgement,
        bool requireConfirmation = true,
        bool verifyAfterWrite = true,
        bool allowManualAddress = false,
        CancellationToken cancellationToken = default);

    Task<Result<MemoryEditorFeatureState>> DisableAsync(
        CancellationToken cancellationToken = default);
}
