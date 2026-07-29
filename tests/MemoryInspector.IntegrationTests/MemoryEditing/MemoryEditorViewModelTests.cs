using System.Runtime.InteropServices;
using MemoryInspector.Application.Configuration;
using MemoryInspector.Application.Memory;
using MemoryInspector.Application.Memory.Editing;
using MemoryInspector.Application.Monitoring;
using MemoryInspector.Common;
using MemoryInspector.Core.Memory;
using MemoryInspector.Core.Memory.Editing;
using MemoryInspector.Core.Monitoring;
using MemoryInspector.Core.Processes;
using MemoryInspector.Core.Scanning;
using MemoryInspector.IntegrationTests.ProcessExplorer;
using MemoryInspector.Wpf.Services;
using MemoryInspector.Wpf.ViewModels;
using MemoryInspector.Windows.Memory;
using MemoryInspector.Windows.Memory.Editing;

namespace MemoryInspector.IntegrationTests.MemoryEditing;

[TestClass]
public sealed class MemoryEditorViewModelTests
{
    private static readonly DateTimeOffset Now = new(
        2026,
        7,
        29,
        14,
        0,
        0,
        TimeSpan.Zero);
    private static readonly MonitoringSessionIdentity Identity = new(
        4242,
        Now.AddHours(-1),
        ProcessArchitecture.X64,
        "MemoryInspector.TestTarget");

    [TestMethod]
    public async Task ConfirmedWriteShowsReadBackAndCreatesHistory()
    {
        var context = CreateContext(enabled: true, confirmations: true);
        using var viewModel = context.ViewModel;
        MemoryWriteCompletedEventArgs? completed = null;
        viewModel.WriteCompleted +=
            (_, eventArgs) => completed = eventArgs;
        await viewModel.InitializeAsync();
        await viewModel.OpenAsync(
            0x1000,
            ScanValueType.Int32,
            MemoryWriteSource.ScanResult);
        viewModel.NewValueText = "20";

        Assert.IsTrue(viewModel.CanWrite);
        Assert.AreEqual("20", viewModel.ParsedValueDisplay);
        Assert.AreEqual("0x00000014", viewModel.HexadecimalPreview);
        Assert.AreEqual("14 00 00 00", viewModel.NewBytesPreview);

        await viewModel.WriteCommand.ExecuteAsync();

        Assert.AreEqual(
            20,
            BitConverter.ToInt32(
                context.Writer.Read(0x1000)!.Value.Span));
        Assert.AreEqual("Success", viewModel.ResultStatusDisplay);
        Assert.AreEqual("Verified", viewModel.VerificationStatusDisplay);
        Assert.AreEqual("14 00 00 00", viewModel.ResultReadBackDisplay);
        Assert.AreEqual(1, viewModel.History.Count);
        Assert.IsNotNull(completed);
        StringAssert.Contains(
            context.Confirmation.Messages.Single(),
            "Address: 0x0000000000001000");
        StringAssert.Contains(
            context.Confirmation.Messages.Single(),
            "Original bytes: 0A 00 00 00");
    }

    [TestMethod]
    public async Task HexadecimalInputShowsParsedValueByteOrderAndCount()
    {
        var context = CreateContext(enabled: true);
        using var viewModel = context.ViewModel;
        await viewModel.OpenAsync(
            0x1000,
            ScanValueType.Int32,
            MemoryWriteSource.WatchWindow);
        viewModel.SelectedInputFormat =
            MemoryEditorInputFormat.Hexadecimal;
        viewModel.NewValueText = "0000002A";

        Assert.AreEqual("42", viewModel.ParsedValueDisplay);
        Assert.AreEqual("0x0000002A", viewModel.HexadecimalPreview);
        Assert.AreEqual("2A 00 00 00", viewModel.NewBytesPreview);
        Assert.AreEqual("LittleEndian", viewModel.ByteOrderDisplay);
        Assert.AreEqual("4 byte(s)", viewModel.WriteByteCountDisplay);
        Assert.IsTrue(viewModel.CanWrite);
    }

    [TestMethod]
    public async Task DisabledFeatureKeepsWriteCommandUnavailable()
    {
        var context = CreateContext(enabled: false);
        using var viewModel = context.ViewModel;
        await viewModel.OpenAsync(
            0x1000,
            ScanValueType.Int32,
            MemoryWriteSource.SavedAddress);
        viewModel.NewValueText = "20";

        Assert.IsFalse(viewModel.CanWrite);
        Assert.IsFalse(viewModel.WriteCommand.CanExecute(null));

        await viewModel.WriteCommand.ExecuteAsync();

        Assert.AreEqual(0, context.Writer.WriteCallCount);
        Assert.AreEqual(
            10,
            BitConverter.ToInt32(
                context.Writer.Read(0x1000)!.Value.Span));
    }

    [TestMethod]
    public async Task UndoDetectsConflictAndRequiresSeparateConfirmation()
    {
        var context = CreateContext(
            enabled: true,
            confirmations: [true, false]);
        using var viewModel = context.ViewModel;
        await viewModel.OpenAsync(
            0x1000,
            ScanValueType.Int32,
            MemoryWriteSource.ScanResult);
        viewModel.NewValueText = "20";
        await viewModel.WriteCommand.ExecuteAsync();
        context.Writer.Seed(0x1000, BitConverter.GetBytes(99));

        await viewModel.UndoLastWriteCommand.ExecuteAsync();

        Assert.AreEqual(
            99,
            BitConverter.ToInt32(
                context.Writer.Read(0x1000)!.Value.Span));
        Assert.AreEqual(1, context.Writer.WriteCallCount);
        Assert.AreEqual(2, context.Confirmation.Titles.Count);
        Assert.AreEqual(
            "Undo conflict",
            context.Confirmation.Titles[1]);
        Assert.AreEqual(1, viewModel.History.Count);
    }

    [TestMethod]
    public async Task HistorySupportsFilterCopyAndFailedRetryPreparation()
    {
        var context = CreateContext(enabled: true, confirmations: true);
        context.Writer.VerificationReadBackOverride =
            BitConverter.GetBytes(99);
        using var viewModel = context.ViewModel;
        await viewModel.OpenAsync(
            0x1000,
            ScanValueType.Int32,
            MemoryWriteSource.SavedAddress);
        viewModel.NewValueText = "20";
        await viewModel.WriteCommand.ExecuteAsync();
        viewModel.HistoryFilterText = "VerificationMismatch";
        viewModel.SelectedHistoryEntry = viewModel.History.Single();

        viewModel.CopyHistoryCommand.Execute(null);
        await viewModel.RetryFailedCommand.ExecuteAsync();

        Assert.IsNotNull(context.Clipboard.Text);
        StringAssert.Contains(
            context.Clipboard.Text,
            "VerificationMismatch");
        Assert.AreEqual("20", viewModel.NewValueText);
        StringAssert.Contains(viewModel.UserNote, "Retry:");
        Assert.AreEqual(
            MemoryWriteSource.SavedAddress.ToString(),
            viewModel.SourceDisplay);
    }

    [TestMethod]
    public async Task LiveTestTargetWriteIsVerifiedThroughUiViewModel()
    {
        using var target =
            await WindowsMemoryWriterIntegrationTests
                .TestTargetProcess.StartAsync();
        var session = target.CreateSession();
        var feature = new StubFeatureService(enabled: true);
        var sessions = new StubSessionService(session);
        var logger = new TestLogger();
        var audit = new InMemoryMemoryWriteAuditService();
        using var writer = new WindowsMemoryWriter(
            sessions,
            TimeProvider.System);
        var writeService = new MemoryWriteService(
            feature,
            sessions,
            writer,
            audit,
            TimeProvider.System);
        using var viewModel = new MemoryEditorViewModel(
            feature,
            sessions,
            new MemoryReaderService(
                sessions,
                new WindowsMemoryReaderProvider(logger)),
            new MemoryRegionService(
                sessions,
                new WindowsMemoryRegionProvider(logger)),
            new MemoryValueSerializer(
                new InvariantScanValueParser()),
            writeService,
            audit,
            new StubAuditExportService(),
            new RecordingConfirmationService(true),
            new NullMemoryEditorFileDialogService(),
            new RecordingClipboardService(),
            logger,
            TimeProvider.System);

        await viewModel.OpenAsync(
            target.Int32Address,
            ScanValueType.Int32,
            MemoryWriteSource.ScanResult);
        viewModel.NewValueText = "13579";
        await viewModel.WriteCommand.ExecuteAsync();
        var values = await target.GetValuesAsync();

        Assert.AreEqual(13_579, values.Integer);
        Assert.AreEqual("Success", viewModel.ResultStatusDisplay);
        Assert.AreEqual(
            "Verified",
            viewModel.VerificationStatusDisplay);
        Assert.AreEqual("0B 35 00 00", viewModel.ResultReadBackDisplay);
    }

    private static TestContext CreateContext(
        bool enabled,
        params bool[] confirmations)
    {
        var session = new MonitoringSession
        {
            SessionId = Guid.NewGuid(),
            Identity = Identity,
            State = MonitoringSessionState.Connected,
            CreatedAt = Now,
            ConnectedAt = Now,
        };
        var feature = new StubFeatureService(enabled);
        var sessionService = new StubSessionService(session);
        var time = new FixedTimeProvider(Now);
        var writer = new MockMemoryWriter(
            session.SessionId,
            Identity,
            time);
        writer.Seed(0x1000, BitConverter.GetBytes(10));
        var audit = new InMemoryMemoryWriteAuditService();
        var writeService = new MemoryWriteService(
            feature,
            sessionService,
            writer,
            audit,
            time);
        var confirmation =
            new RecordingConfirmationService(confirmations);
        var clipboard = new RecordingClipboardService();
        var viewModel = new MemoryEditorViewModel(
            feature,
            sessionService,
            new MockBackedReaderService(writer),
            new WritableRegionService(),
            new MemoryValueSerializer(
                new InvariantScanValueParser()),
            writeService,
            audit,
            new StubAuditExportService(),
            confirmation,
            new NullMemoryEditorFileDialogService(),
            clipboard,
            new TestLogger(),
            time);
        return new TestContext(
            viewModel,
            writer,
            confirmation,
            clipboard);
    }

    private sealed record TestContext(
        MemoryEditorViewModel ViewModel,
        MockMemoryWriter Writer,
        RecordingConfirmationService Confirmation,
        RecordingClipboardService Clipboard);

    private sealed class StubFeatureService :
        IMemoryEditorFeatureService
    {
        public StubFeatureService(bool enabled)
        {
            State = CreateState(enabled);
        }

        public MemoryEditorFeatureState State { get; private set; }

        public event EventHandler<MemoryEditorFeatureChangedEventArgs>?
            StateChanged;

        public Result<MemoryEditorFeatureState> Initialize(
            AppSettings settings)
        {
            return Result<MemoryEditorFeatureState>.Success(State);
        }

        public Task<Result<MemoryEditorFeatureState>> EnableAsync(
            MemoryEditorEnablementAcknowledgement acknowledgement,
            bool requireConfirmation = true,
            bool verifyAfterWrite = true,
            bool allowManualAddress = false,
            CancellationToken cancellationToken = default)
        {
            State = new MemoryEditorFeatureState(
                new MemoryEditorSettings
                {
                    Enabled = true,
                    EnabledAt = Now,
                    RequireConfirmation = requireConfirmation,
                    VerifyAfterWrite = verifyAfterWrite,
                    AllowManualAddress = allowManualAddress,
                });
            StateChanged?.Invoke(
                this,
                new MemoryEditorFeatureChangedEventArgs(State));
            return Task.FromResult(
                Result<MemoryEditorFeatureState>.Success(State));
        }

        public Task<Result<MemoryEditorFeatureState>> DisableAsync(
            CancellationToken cancellationToken = default)
        {
            State = CreateState(enabled: false);
            StateChanged?.Invoke(
                this,
                new MemoryEditorFeatureChangedEventArgs(State));
            return Task.FromResult(
                Result<MemoryEditorFeatureState>.Success(State));
        }

        private static MemoryEditorFeatureState CreateState(
            bool enabled)
        {
            return new MemoryEditorFeatureState(
                new MemoryEditorSettings
                {
                    Enabled = enabled,
                    EnabledAt = enabled ? Now : null,
                    RequireConfirmation = true,
                    VerifyAfterWrite = true,
                    AllowManualAddress = true,
                });
        }
    }

    private sealed class StubSessionService(
        MonitoringSession session) : IMonitoringSessionService
    {
        public MonitoringSession? CurrentSession { get; } = session;

        public event EventHandler<MonitoringSessionChangedEventArgs>?
            SessionChanged
        {
            add { }
            remove { }
        }

        public Task<Result<MonitoringSession>> StartAsync(
            MonitoringSessionIdentity identity,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<MonitoringSession>> CheckLivenessAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result> StopAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class MockBackedReaderService(
        MockMemoryWriter writer) : IMemoryReaderService
    {
        public Task<Result<MemoryReadResult>> ReadAsync(
            ulong address,
            int length,
            MemoryReadOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var value = writer.Read(address);

            if (!value.HasValue)
            {
                return Task.FromResult(
                    Result<MemoryReadResult>.Failure(
                        new Error(
                            ErrorCode.NotFound,
                            "Address not seeded.")));
            }

            return Task.FromResult(
                Result<MemoryReadResult>.Success(
                    new MemoryReadResult(
                        new MemoryReadRequest(address, length),
                        value.Value.Span[..Math.Min(
                            length,
                            value.Value.Length)])));
        }

        public async Task<Result<T>> TryReadAsync<T>(
            ulong address,
            MemoryReadOptions? options = null,
            CancellationToken cancellationToken = default)
            where T : unmanaged
        {
            var read = await ReadAsync(
                address,
                Marshal.SizeOf<T>(),
                options,
                cancellationToken);
            return read.IsSuccess && read.Value.IsComplete
                ? Result<T>.Success(
                    MemoryMarshal.Read<T>(read.Value.Data.Span))
                : Result<T>.Failure(
                    read.IsFailure
                        ? read.Error
                        : new Error(
                            ErrorCode.NativeApi,
                            "Incomplete value."));
        }

        public Task<Result<MemoryBatchReadResult>> ReadBatchAsync(
            IEnumerable<MemoryReadRequest> requests,
            MemoryReadOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class WritableRegionService :
        IMemoryRegionService
    {
        public Task<Result<MemoryRegionQueryResult>> GetRegionsAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                Result<MemoryRegionQueryResult>.Success(
                    new MemoryRegionQueryResult(
                    [
                        new MemoryRegion(
                            0x1000,
                            0x1000,
                            0x1000,
                            MemoryRegionState.Committed,
                            MemoryRegionType.Private,
                            MemoryProtection.ReadWrite),
                    ])));
        }
    }

    private sealed class RecordingConfirmationService(
        params bool[] responses) : IUserConfirmationService
    {
        private readonly Queue<bool> _responses = new(responses);

        public List<string> Titles { get; } = [];

        public List<string> Messages { get; } = [];

        public bool Confirm(string title, string message)
        {
            Titles.Add(title);
            Messages.Add(message);
            return _responses.Count == 0 ||
                   _responses.Dequeue();
        }
    }

    private sealed class RecordingClipboardService :
        IClipboardService
    {
        public string? Text { get; private set; }

        public Result SetText(string text)
        {
            Text = text;
            return Result.Success();
        }
    }

    private sealed class StubAuditExportService :
        IMemoryWriteAuditExportService
    {
        public Task<Result> ExportSummaryAsync(
            string path,
            IReadOnlyList<MemoryWriteAuditEntry> entries,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Success());
        }
    }

    private sealed class NullMemoryEditorFileDialogService :
        IMemoryEditorFileDialogService
    {
        public string? SelectAuditExportFile(
            string suggestedFileName) => null;
    }

    private sealed class FixedTimeProvider(
        DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
