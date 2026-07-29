using System.Diagnostics;
using System.Globalization;
using MemoryInspector.Application.Configuration;
using MemoryInspector.Application.Memory.Editing;
using MemoryInspector.Application.Monitoring;
using MemoryInspector.Common;
using MemoryInspector.Core.Memory.Editing;
using MemoryInspector.Core.Monitoring;
using MemoryInspector.Core.Processes;
using MemoryInspector.Core.Scanning;
using MemoryInspector.Windows.Memory.Editing;

namespace MemoryInspector.IntegrationTests.MemoryEditing;

[TestClass]
public sealed class WindowsMemoryWriterIntegrationTests
{
    [TestMethod]
    public async Task AuthorizedTestTargetSupportsVerifiedInt32AndFloatWrites()
    {
        using var target = await TestTargetProcess.StartAsync();
        var session = target.CreateSession();
        using var writer = new WindowsMemoryWriter(
            new StubSessionService(session),
            TimeProvider.System);
        var audit = new InMemoryMemoryWriteAuditService();
        var service = CreateService(session, writer, audit);

        var integerResult = await service.WriteAsync(
            CreateRequest(
                session,
                target.Int32Address,
                ScanValueType.Int32,
                BitConverter.GetBytes(987_654_321),
                BitConverter.GetBytes(123_456_789),
                "987654321"));
        var floatResult = await service.WriteAsync(
            CreateRequest(
                session,
                target.FloatAddress,
                ScanValueType.Float,
                BitConverter.GetBytes(45.25F),
                BitConverter.GetBytes(12.5F),
                "45.25"));
        var values = await target.GetValuesAsync();
        var auditEntries = await audit.ReadRecentAsync();

        Assert.IsTrue(integerResult.Success);
        Assert.IsTrue(floatResult.Success);
        Assert.AreEqual(
            MemoryWriteVerificationStatus.Verified,
            integerResult.Verification.Status);
        Assert.AreEqual(
            MemoryWriteVerificationStatus.Verified,
            floatResult.Verification.Status);
        Assert.AreEqual(987_654_321, values.Integer);
        Assert.AreEqual(45.25F, values.Floating);
        Assert.IsTrue(auditEntries.IsSuccess);
        Assert.AreEqual(2, auditEntries.Value.Count);
        Assert.IsTrue(
            auditEntries.Value.All(entry => entry.Success));
    }

    [TestMethod]
    public async Task ExitedTestTargetCannotBeWritten()
    {
        using var target = await TestTargetProcess.StartAsync();
        var session = target.CreateSession();
        using var writer = new WindowsMemoryWriter(
            new StubSessionService(session),
            TimeProvider.System);
        target.Stop();

        var result = await writer.WriteAsync(
            CreateRequest(
                session,
                target.Int32Address,
                ScanValueType.Int32,
                BitConverter.GetBytes(1),
                BitConverter.GetBytes(123_456_789),
                "1"));

        Assert.IsFalse(result.Success);
        Assert.AreEqual(
            MemoryWriteFailureReason.TargetExited,
            result.FailureReason);
    }

    private static MemoryWriteService CreateService(
        MonitoringSession session,
        IMemoryWriter writer,
        IMemoryWriteAuditService audit)
    {
        return new MemoryWriteService(
            new EnabledFeatureService(),
            new StubSessionService(session),
            writer,
            audit,
            TimeProvider.System);
    }

    private static MemoryWriteRequest CreateRequest(
        MonitoringSession session,
        ulong address,
        ScanValueType valueType,
        byte[] requested,
        byte[] expected,
        string input)
    {
        return new MemoryWriteRequest(
            session.SessionId,
            session.Identity,
            address,
            valueType,
            input,
            requested,
            expected,
            hasExpectedOriginalValue: true,
            verifyAfterWrite: true,
            MemoryWriteSource.SavedAddress,
            "Owned Test Target integration test",
            TimeProvider.System.GetUtcNow());
    }

    internal sealed class TestTargetProcess : IDisposable
    {
        private readonly Process _process;
        private bool _stopped;

        private TestTargetProcess(
            Process process,
            ulong int32Address,
            ulong floatAddress)
        {
            _process = process;
            Int32Address = int32Address;
            FloatAddress = floatAddress;
        }

        public ulong Int32Address { get; }

        public ulong FloatAddress { get; }

        public static async Task<TestTargetProcess> StartAsync()
        {
            var executable = FindTestTargetExecutable();
            var process = Process.Start(
                new ProcessStartInfo
                {
                    FileName = executable,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                }) ?? throw new InvalidOperationException(
                    "The MemoryInspector Test Target could not be started.");
            var ready = await process.StandardOutput
                .ReadLineAsync()
                .WaitAsync(TimeSpan.FromSeconds(10));
            var parts = ready?.Split('|');

            if (parts is not { Length: 4 } ||
                parts[0] != "READY" ||
                !ulong.TryParse(
                    parts[2],
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out var int32Address) ||
                !ulong.TryParse(
                    parts[3],
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out var floatAddress))
            {
                var error = await process.StandardError.ReadToEndAsync();
                process.Dispose();
                throw new InvalidOperationException(
                    $"The Test Target returned an invalid handshake: " +
                    $"{ready}. {error}");
            }

            return new TestTargetProcess(
                process,
                int32Address,
                floatAddress);
        }

        public MonitoringSession CreateSession()
        {
            var identity = new MonitoringSessionIdentity(
                _process.Id,
                new DateTimeOffset(_process.StartTime),
                ProcessArchitecture.X64,
                _process.ProcessName);
            var now = TimeProvider.System.GetUtcNow();
            return new MonitoringSession
            {
                SessionId = Guid.NewGuid(),
                Identity = identity,
                State = MonitoringSessionState.Connected,
                CreatedAt = now,
                ConnectedAt = now,
            };
        }

        public async Task<(int Integer, float Floating)>
            GetValuesAsync()
        {
            await _process.StandardInput.WriteLineAsync("GET");
            var response = await _process.StandardOutput
                .ReadLineAsync()
                .WaitAsync(TimeSpan.FromSeconds(5));
            var parts = response?.Split('|');

            if (parts is not { Length: 3 } ||
                parts[0] != "VALUES")
            {
                throw new InvalidOperationException(
                    $"The Test Target returned invalid values: {response}.");
            }

            return (
                int.Parse(
                    parts[1],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture),
                float.Parse(
                    parts[2],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture));
        }

        public void Stop()
        {
            if (_stopped)
            {
                return;
            }

            _process.StandardInput.WriteLine("EXIT");

            if (!_process.WaitForExit(5_000))
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit();
            }

            _stopped = true;
        }

        public void Dispose()
        {
            if (!_stopped && !_process.HasExited)
            {
                Stop();
            }

            _process.Dispose();
        }

        private static string FindTestTargetExecutable()
        {
            var current = new DirectoryInfo(
                AppContext.BaseDirectory);

            while (current is not null &&
                   !File.Exists(
                       Path.Combine(
                           current.FullName,
                           "MemoryInspector.slnx")))
            {
                current = current.Parent;
            }

            if (current is null)
            {
                throw new FileNotFoundException(
                    "The MemoryInspector repository root was not found.");
            }

            var configuration = AppContext.BaseDirectory.Contains(
                $"{Path.DirectorySeparatorChar}Release" +
                $"{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase)
                    ? "Release"
                    : "Debug";
            var executable = Path.Combine(
                current.FullName,
                "tests",
                "MemoryInspector.TestTarget",
                "bin",
                configuration,
                "net10.0-windows",
                "MemoryInspector.TestTarget.exe");

            return File.Exists(executable)
                ? executable
                : throw new FileNotFoundException(
                    "The MemoryInspector Test Target executable " +
                    "was not built.",
                    executable);
        }
    }

    private sealed class EnabledFeatureService :
        IMemoryEditorFeatureService
    {
        public MemoryEditorFeatureState State { get; } =
            new(
                new MemoryEditorSettings
                {
                    Enabled = true,
                    EnabledAt = TimeProvider.System.GetUtcNow(),
                    RequireConfirmation = true,
                    VerifyAfterWrite = true,
                    AllowManualAddress = false,
                });

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
}
