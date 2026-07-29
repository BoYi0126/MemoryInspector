using MemoryInspector.Application.Configuration;
using MemoryInspector.Common;

namespace MemoryInspector.Application.Memory.Editing;

public sealed class MemoryEditorFeatureService(
    ISettingsService settingsService,
    TimeProvider timeProvider) :
    IMemoryEditorFeatureService,
    IDisposable
{
    private readonly ISettingsService _settingsService =
        Guard.NotNull(settingsService);
    private readonly TimeProvider _timeProvider =
        Guard.NotNull(timeProvider);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AppSettings? _appSettings;
    private MemoryEditorFeatureState _state = new(
        MemoryEditorSettings.CreateDefault());
    private bool _disposed;

    public MemoryEditorFeatureState State =>
        Volatile.Read(ref _state);

    public event EventHandler<MemoryEditorFeatureChangedEventArgs>?
        StateChanged;

    public Result<MemoryEditorFeatureState> Initialize(
        AppSettings settings)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(settings);
        var validation = settings.Validate();

        if (validation.IsFailure)
        {
            return Result<MemoryEditorFeatureState>.Failure(
                validation.Error);
        }

        _appSettings = settings;
        var state = new MemoryEditorFeatureState(
            settings.MemoryEditor);
        Volatile.Write(ref _state, state);
        StateChanged?.Invoke(
            this,
            new MemoryEditorFeatureChangedEventArgs(state));
        return Result<MemoryEditorFeatureState>.Success(state);
    }

    public async Task<Result<MemoryEditorFeatureState>> EnableAsync(
        MemoryEditorEnablementAcknowledgement acknowledgement,
        bool requireConfirmation = true,
        bool verifyAfterWrite = true,
        bool allowManualAddress = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(acknowledgement);

        if (!acknowledgement.AcknowledgesRisk ||
            !acknowledgement.ConfirmsAuthorizedTargetsOnly)
        {
            return Result<MemoryEditorFeatureState>.Failure(
                new Error(
                    ErrorCode.Validation,
                    "Memory Editor requires both risk and " +
                    "authorized-target acknowledgements."));
        }

        return await ChangeAsync(
            new MemoryEditorSettings
            {
                Enabled = true,
                RequireConfirmation = requireConfirmation,
                VerifyAfterWrite = verifyAfterWrite,
                AllowManualAddress = allowManualAddress,
                EnabledAt = _timeProvider.GetUtcNow(),
            },
            cancellationToken);
    }

    public async Task<Result<MemoryEditorFeatureState>> DisableAsync(
        CancellationToken cancellationToken = default)
    {
        var current = State.Settings;
        return await ChangeAsync(
            current with
            {
                Enabled = false,
                EnabledAt = null,
            },
            cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _gate.Dispose();
        _disposed = true;
    }

    private async Task<Result<MemoryEditorFeatureState>> ChangeAsync(
        MemoryEditorSettings settings,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            await _gate.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
            when (cancellationToken.IsCancellationRequested)
        {
            return Cancelled(exception);
        }

        MemoryEditorFeatureState? changed = null;

        try
        {
            if (_appSettings is null)
            {
                return Result<MemoryEditorFeatureState>.Failure(
                    new Error(
                        ErrorCode.InvalidState,
                        "Memory Editor feature state has not " +
                        "been initialized."));
            }

            var updatedAppSettings = _appSettings with
            {
                MemoryEditor = settings,
            };
            var save = await _settingsService.SaveAsync(
                    updatedAppSettings,
                    cancellationToken)
                .ConfigureAwait(false);

            if (save.IsFailure)
            {
                return Result<MemoryEditorFeatureState>.Failure(
                    save.Error);
            }

            _appSettings = updatedAppSettings;
            changed = new MemoryEditorFeatureState(settings);
            Volatile.Write(ref _state, changed);
            return Result<MemoryEditorFeatureState>.Success(changed);
        }
        finally
        {
            _gate.Release();

            if (changed is not null)
            {
                StateChanged?.Invoke(
                    this,
                    new MemoryEditorFeatureChangedEventArgs(changed));
            }
        }
    }

    private static Result<MemoryEditorFeatureState> Cancelled(
        OperationCanceledException exception)
    {
        return Result<MemoryEditorFeatureState>.Failure(
            new Error(
                ErrorCode.Cancelled,
                "Changing Memory Editor feature state was cancelled.",
                exception));
    }
}
