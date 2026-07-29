using MemoryInspector.Application.Configuration;
using MemoryInspector.Application.Memory.Editing;
using MemoryInspector.Application.Monitoring;
using MemoryInspector.Common;
using MemoryInspector.Core.Memory.Editing;
using MemoryInspector.Core.Monitoring;
using MemoryInspector.Core.Processes;
using MemoryInspector.Core.Scanning;

namespace MemoryInspector.IntegrationTests.MemoryEditing;

[TestClass]
public sealed class MemoryEditorFoundationTests
{
    private static readonly DateTimeOffset Now = new(
        2026,
        7,
        29,
        12,
        0,
        0,
        TimeSpan.Zero);
    private static readonly MonitoringSessionIdentity Identity = new(
        42,
        new DateTimeOffset(
            2026,
            7,
            29,
            8,
            0,
            0,
            TimeSpan.Zero),
        ProcessArchitecture.X64,
        "AuthorizedTarget.exe");

    [TestMethod]
    public async Task FeatureDefaultsDisabledAndRequiresBothAcknowledgements()
    {
        var settingsStore = new RecordingSettingsService();
        using var service = new MemoryEditorFeatureService(
            settingsStore,
            new FixedTimeProvider(Now));
        var initial = service.Initialize(
            AppSettings.CreateDefault());

        var rejected = await service.EnableAsync(
            new MemoryEditorEnablementAcknowledgement(
                AcknowledgesRisk: true,
                ConfirmsAuthorizedTargetsOnly: false));

        Assert.IsTrue(initial.IsSuccess);
        Assert.IsFalse(initial.Value.IsEnabled);
        Assert.IsTrue(rejected.IsFailure);
        Assert.IsFalse(service.State.IsEnabled);
        Assert.AreEqual(0, settingsStore.SaveCount);
        Assert.IsTrue(
            MemoryEditorFeatureState.AuthorizedUseStatement.Contains(
                "authorized",
                StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task FeatureEnablementPersistsRiskSensitiveDefaultsAndTime()
    {
        var settingsStore = new RecordingSettingsService();
        using var service = new MemoryEditorFeatureService(
            settingsStore,
            new FixedTimeProvider(Now));
        _ = service.Initialize(AppSettings.CreateDefault());

        var enabled = await service.EnableAsync(
            new MemoryEditorEnablementAcknowledgement(
                AcknowledgesRisk: true,
                ConfirmsAuthorizedTargetsOnly: true));
        var disabled = await service.DisableAsync();

        Assert.IsTrue(enabled.IsSuccess);
        Assert.IsTrue(enabled.Value.IsEnabled);
        Assert.AreEqual(Now, enabled.Value.EnabledAt);
        Assert.IsTrue(enabled.Value.Settings.RequireConfirmation);
        Assert.IsTrue(enabled.Value.Settings.VerifyAfterWrite);
        Assert.IsFalse(enabled.Value.Settings.AllowManualAddress);
        Assert.IsTrue(disabled.IsSuccess);
        Assert.IsFalse(disabled.Value.IsEnabled);
        Assert.IsNull(disabled.Value.EnabledAt);
        Assert.AreEqual(2, settingsStore.SaveCount);
    }

    [TestMethod]
    public async Task DisabledFeatureRejectsWriteBeforeWriterAndAuditsAttempt()
    {
        var session = CreateSession();
        var writer = new MockMemoryWriter(
            session.SessionId,
            Identity,
            new FixedTimeProvider(Now));
        writer.Seed(0x1000, BitConverter.GetBytes(10));
        var audit = new InMemoryMemoryWriteAuditService();
        var service = CreateWriteService(
            enabled: false,
            session,
            writer,
            audit);

        var result = await service.WriteAsync(
            CreateRequest(session, requested: 20));
        var entries = await audit.ReadRecentAsync();

        Assert.IsFalse(result.Success);
        Assert.AreEqual(
            MemoryWriteFailureReason.FeatureDisabled,
            result.FailureReason);
        Assert.AreEqual(0, writer.WriteCallCount);
        Assert.AreEqual(1, entries.Value.Count);
        Assert.IsFalse(entries.Value[0].Success);
        Assert.AreEqual(
            MemoryWriteFailureReason.FeatureDisabled,
            entries.Value[0].FailureReason);
    }

    [TestMethod]
    public async Task ExpectedOriginalMismatchDoesNotChangeMockMemory()
    {
        var session = CreateSession();
        var writer = new MockMemoryWriter(
            session.SessionId,
            Identity,
            new FixedTimeProvider(Now));
        writer.Seed(0x1000, BitConverter.GetBytes(10));
        var audit = new InMemoryMemoryWriteAuditService();
        var service = CreateWriteService(
            enabled: true,
            session,
            writer,
            audit);

        var result = await service.WriteAsync(
            CreateRequest(
                session,
                requested: 20,
                expected: 11));

        Assert.IsFalse(result.Success);
        Assert.AreEqual(
            MemoryWriteFailureReason.OriginalValueMismatch,
            result.FailureReason);
        Assert.AreEqual(
            10,
            BitConverter.ToInt32(
                writer.Read(0x1000)!.Value.ToArray()));
        var entry = (await audit.ReadRecentAsync()).Value.Single();
        Assert.AreEqual(
            MemoryWriteFailureReason.OriginalValueMismatch,
            entry.FailureReason);
    }

    [TestMethod]
    public async Task SessionIdentityMismatchIsRejectedBeforeMockWriter()
    {
        var session = CreateSession();
        var writer = new MockMemoryWriter(
            session.SessionId,
            Identity,
            new FixedTimeProvider(Now));
        writer.Seed(0x1000, BitConverter.GetBytes(10));
        var audit = new InMemoryMemoryWriteAuditService();
        var service = CreateWriteService(
            enabled: true,
            session,
            writer,
            audit);
        var mismatched = session with
        {
            SessionId = Guid.NewGuid(),
        };

        var result = await service.WriteAsync(
            CreateRequest(mismatched, requested: 20));

        Assert.IsFalse(result.Success);
        Assert.AreEqual(
            MemoryWriteFailureReason.SessionInvalid,
            result.FailureReason);
        Assert.AreEqual(0, writer.WriteCallCount);
        Assert.AreEqual(
            MemoryWriteFailureReason.SessionInvalid,
            (await audit.ReadRecentAsync())
                .Value.Single().FailureReason);
    }

    [TestMethod]
    public async Task MockWriteReturnsVerifiedReadBackAndSuccessAudit()
    {
        var session = CreateSession();
        var writer = new MockMemoryWriter(
            session.SessionId,
            Identity,
            new FixedTimeProvider(Now));
        writer.Seed(0x1000, BitConverter.GetBytes(10));
        var audit = new InMemoryMemoryWriteAuditService();
        var service = CreateWriteService(
            enabled: true,
            session,
            writer,
            audit);

        var result = await service.WriteAsync(
            CreateRequest(
                session,
                requested: 20,
                expected: 10));

        Assert.IsTrue(result.Success);
        Assert.AreEqual(4, result.WrittenByteCount);
        Assert.AreEqual(
            MemoryWriteVerificationStatus.Verified,
            result.Verification.Status);
        Assert.AreEqual(
            20,
            BitConverter.ToInt32(
                result.ReadBackValue!.Value.ToArray()));
        var entry = (await audit.ReadRecentAsync()).Value.Single();
        Assert.IsTrue(entry.Success);
        Assert.AreEqual(
            MemoryWriteVerificationStatus.Verified,
            entry.VerificationStatus);
        Assert.AreEqual(MemoryWriteSource.SavedAddress, entry.Source);
    }

    [TestMethod]
    public async Task MockVerificationMismatchIsNotReportedAsSuccess()
    {
        var session = CreateSession();
        var writer = new MockMemoryWriter(
            session.SessionId,
            Identity,
            new FixedTimeProvider(Now))
        {
            VerificationReadBackOverride =
                BitConverter.GetBytes(99),
        };
        writer.Seed(0x1000, BitConverter.GetBytes(10));
        var audit = new InMemoryMemoryWriteAuditService();
        var service = CreateWriteService(
            enabled: true,
            session,
            writer,
            audit);

        var result = await service.WriteAsync(
            CreateRequest(session, requested: 20));

        Assert.IsFalse(result.Success);
        Assert.AreEqual(
            MemoryWriteFailureReason.VerificationMismatch,
            result.FailureReason);
        Assert.AreEqual(
            MemoryWriteVerificationStatus.Mismatch,
            result.Verification.Status);
    }

    [TestMethod]
    public async Task AuditFailureIsSurfacedEvenWhenMockWriteOccurred()
    {
        var session = CreateSession();
        var writer = new MockMemoryWriter(
            session.SessionId,
            Identity,
            new FixedTimeProvider(Now));
        writer.Seed(0x1000, BitConverter.GetBytes(10));
        var service = CreateWriteService(
            enabled: true,
            session,
            writer,
            new FailingAuditService());

        var result = await service.WriteAsync(
            CreateRequest(session, requested: 20));

        Assert.IsFalse(result.Success);
        Assert.AreEqual(
            MemoryWriteFailureReason.AuditFailed,
            result.FailureReason);
        Assert.AreEqual(4, result.WrittenByteCount);
        Assert.AreEqual(
            20,
            BitConverter.ToInt32(
                writer.Read(0x1000)!.Value.ToArray()));
    }

    [TestMethod]
    public async Task ManualAddressIsRejectedByDefault()
    {
        var session = CreateSession();
        var writer = new MockMemoryWriter(
            session.SessionId,
            Identity,
            new FixedTimeProvider(Now));
        writer.Seed(0x1000, BitConverter.GetBytes(10));
        var audit = new InMemoryMemoryWriteAuditService();
        var service = CreateWriteService(
            enabled: true,
            session,
            writer,
            audit);

        var result = await service.WriteAsync(
            CreateRequest(
                session,
                requested: 20,
                source: MemoryWriteSource.ManualAddress));

        Assert.IsFalse(result.Success);
        Assert.AreEqual(
            MemoryWriteFailureReason.ManualAddressDisabled,
            result.FailureReason);
        Assert.AreEqual(0, writer.WriteCallCount);
    }

    [TestMethod]
    public async Task DeniedAndNoOpWritersProvideNativeFreeTestBoundaries()
    {
        var session = CreateSession();
        var request = CreateRequest(session, requested: 20);
        var timeProvider = new FixedTimeProvider(Now);

        var denied = await new DeniedMemoryWriter(timeProvider)
            .WriteAsync(request);
        var noOp = await new NoOpMemoryWriter(timeProvider)
            .WriteAsync(request);

        Assert.IsFalse(denied.Success);
        Assert.AreEqual(
            MemoryWriteFailureReason.WriterDenied,
            denied.FailureReason);
        Assert.AreEqual(ErrorCode.AccessDenied, denied.Error.Code);
        Assert.IsTrue(noOp.Success);
        Assert.AreEqual(
            MemoryWriteVerificationStatus.Verified,
            noOp.Verification.Status);
    }

    private static MemoryWriteService CreateWriteService(
        bool enabled,
        MonitoringSession session,
        IMemoryWriter writer,
        IMemoryWriteAuditService audit)
    {
        return new MemoryWriteService(
            new StubFeatureService(
                new MemoryEditorSettings
                {
                    Enabled = enabled,
                    EnabledAt = enabled ? Now : null,
                    RequireConfirmation = true,
                    VerifyAfterWrite = true,
                    AllowManualAddress = false,
                }),
            new StubSessionService(session),
            writer,
            audit,
            new FixedTimeProvider(Now));
    }

    private static MemoryWriteRequest CreateRequest(
        MonitoringSession session,
        int requested,
        int? expected = null,
        MemoryWriteSource source =
            MemoryWriteSource.SavedAddress)
    {
        var expectedBytes = expected.HasValue
            ? BitConverter.GetBytes(expected.Value)
            : Array.Empty<byte>();
        return new MemoryWriteRequest(
            session.SessionId,
            session.Identity,
            0x1000,
            ScanValueType.Int32,
            requested.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            BitConverter.GetBytes(requested),
            expectedBytes,
            expected.HasValue,
            verifyAfterWrite: true,
            source,
            "Foundation test",
            Now);
    }

    private static MonitoringSession CreateSession()
    {
        return new MonitoringSession
        {
            SessionId = Guid.NewGuid(),
            Identity = Identity,
            State = MonitoringSessionState.Connected,
            CreatedAt = Now,
            ConnectedAt = Now,
        };
    }

    private sealed class FixedTimeProvider(
        DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class RecordingSettingsService : ISettingsService
    {
        public int SaveCount { get; private set; }

        public AppSettings? SavedSettings { get; private set; }

        public Task<Result<AppSettings>> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                Result<AppSettings>.Success(
                    AppSettings.CreateDefault()));
        }

        public Task<Result> SaveAsync(
            AppSettings settings,
            CancellationToken cancellationToken = default)
        {
            SavedSettings = settings;
            SaveCount++;
            return Task.FromResult(Result.Success());
        }
    }

    private sealed class StubFeatureService(
        MemoryEditorSettings settings) :
        IMemoryEditorFeatureService
    {
        public MemoryEditorFeatureState State { get; } =
            new(settings);

        public event EventHandler<MemoryEditorFeatureChangedEventArgs>?
            StateChanged
        {
            add { }
            remove { }
        }

        public Result<MemoryEditorFeatureState> Initialize(
            AppSettings settings)
        {
            throw new NotSupportedException();
        }

        public Task<Result<MemoryEditorFeatureState>> EnableAsync(
            MemoryEditorEnablementAcknowledgement acknowledgement,
            bool requireConfirmation = true,
            bool verifyAfterWrite = true,
            bool allowManualAddress = false,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<Result<MemoryEditorFeatureState>> DisableAsync(
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
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
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<Result<MonitoringSession>> CheckLivenessAsync(
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<Result> StopAsync(
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FailingAuditService :
        IMemoryWriteAuditService
    {
        public Task<Result> RecordAsync(
            MemoryWriteAuditEntry entry,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                Result.Failure(
                    new Error(
                        ErrorCode.Io,
                        "Audit storage failed.")));
        }

        public Task<Result<IReadOnlyList<MemoryWriteAuditEntry>>>
            ReadRecentAsync(
                int maximumCount = 1_000,
                CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
