using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using MemoryInspector.Common;
using MemoryInspector.Core.Monitoring;
using MemoryInspector.Core.Processes;
using MemoryInspector.Windows.Memory;
using MemoryInspector.Windows.ProcessInspection;
using MemoryInspector.Windows.Tests.Configuration;

namespace MemoryInspector.Windows.Tests.ProcessInspection;

[TestClass]
public sealed class WindowsProcessDetailsProviderTests
{
    private static readonly MonitoringSessionIdentity Identity = new(
        42,
        new DateTimeOffset(
            2026,
            7,
            29,
            8,
            30,
            0,
            TimeSpan.Zero),
        ProcessArchitecture.X64,
        "Target");

    [TestMethod]
    public async Task MapsModuleAndThreadFields()
    {
        var source = new FakeSource(
            modules:
            [
                new FakeModule(),
            ],
            threads:
            [
                new FakeThread(),
            ]);
        var provider = CreateProvider(source);

        var modules = await provider.GetModulesAsync(Identity);
        var threads = await provider.GetThreadsAsync(Identity);

        Assert.IsTrue(modules.IsSuccess);
        Assert.AreEqual("sample.dll",
            modules.Value.Modules.Single().Name);
        Assert.AreEqual(0x140000000UL,
            modules.Value.Modules.Single().BaseAddress);
        Assert.AreEqual(4096UL,
            modules.Value.Modules.Single().Size);
        Assert.AreEqual("1.2.3.4",
            modules.Value.Modules.Single().Version);
        Assert.IsTrue(threads.IsSuccess);
        Assert.AreEqual(123,
            threads.Value.Threads.Single().ThreadId);
        Assert.AreEqual("Running",
            threads.Value.Threads.Single().State);
        Assert.AreEqual(TimeSpan.FromSeconds(2),
            threads.Value.Threads.Single().CpuTime);
    }

    [TestMethod]
    public async Task ModuleFieldFailureKeepsRowAndReportsWarning()
    {
        var module = new FakeModule
        {
            GetPathValue = () =>
                throw new Win32Exception(5),
            GetVersionValue = () =>
                throw new InvalidOperationException(),
        };
        var logger = new RecordingLogger();
        var provider = CreateProvider(
            new FakeSource(modules: [module]),
            logger: logger);

        var result = await provider.GetModulesAsync(Identity);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1, result.Value.Modules.Count);
        Assert.IsNull(result.Value.Modules[0].Path);
        Assert.IsNull(result.Value.Modules[0].Version);
        Assert.AreEqual(2,
            result.Value.Modules[0].Warnings.Count);
        Assert.IsTrue(result.Value.IsPartial);
        Assert.AreEqual(2, logger.Entries.Count);
    }

    [TestMethod]
    public async Task ThreadFieldFailureKeepsRowAndReportsWarning()
    {
        var thread = new FakeThread
        {
            GetStartTimeValue = () =>
                throw new UnauthorizedAccessException(),
            GetCpuTimeValue = () =>
                throw new InvalidOperationException(),
        };
        var provider = CreateProvider(
            new FakeSource(threads: [thread]));

        var result = await provider.GetThreadsAsync(Identity);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1, result.Value.Threads.Count);
        Assert.IsNull(result.Value.Threads[0].StartTime);
        Assert.IsNull(result.Value.Threads[0].CpuTime);
        Assert.AreEqual(2,
            result.Value.Threads[0].Warnings.Count);
        Assert.IsTrue(result.Value.IsPartial);
    }

    [TestMethod]
    public async Task EnumerationFailureAfterItemReturnsPartialList()
    {
        var source = new FakeSource(
            modulesFactory: EnumerateThenFail);
        var provider = CreateProvider(source);

        var result = await provider.GetModulesAsync(Identity);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1, result.Value.Modules.Count);
        Assert.AreEqual(1, result.Value.Warnings.Count);
        Assert.IsTrue(result.Value.IsPartial);
    }

    [TestMethod]
    public async Task IdentityFailurePreventsEnumeration()
    {
        var source = new FakeSource();
        var provider = CreateProvider(
            source,
            new StubIdentityValidator(
                Result.Failure(
                    new Error(
                        ErrorCode.InvalidState,
                        "Identity changed."))));

        var result = await provider.GetThreadsAsync(Identity);

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(ErrorCode.InvalidState,
            result.Error.Code);
        Assert.AreEqual(0, source.ThreadEnumerationCount);
    }

    [TestMethod]
    public async Task LiveProviderEnumeratesCurrentModulesAndThreads()
    {
        using var process = Process.GetCurrentProcess();
        var provider = new WindowsProcessDetailsProvider(
            new RecordingLogger());
        var identity = new MonitoringSessionIdentity(
            process.Id,
            new DateTimeOffset(process.StartTime),
            RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X86 => ProcessArchitecture.X86,
                Architecture.X64 => ProcessArchitecture.X64,
                Architecture.Arm => ProcessArchitecture.Arm32,
                Architecture.Arm64 => ProcessArchitecture.Arm64,
                _ => throw new PlatformNotSupportedException(),
            },
            process.ProcessName);

        var modules = await provider.GetModulesAsync(identity);
        var threads = await provider.GetThreadsAsync(identity);

        Assert.IsTrue(
            modules.IsSuccess,
            modules.IsFailure
                ? modules.Error.ToDisplayMessage()
                : null);
        Assert.IsTrue(
            threads.IsSuccess,
            threads.IsFailure
                ? threads.Error.ToDisplayMessage()
                : null);
        Assert.IsTrue(modules.Value.Modules.Count > 0);
        Assert.IsTrue(threads.Value.Threads.Count > 0);
        Assert.IsTrue(modules.Value.Modules.Any(module =>
            module.Name.EndsWith(
                ".exe",
                StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(threads.Value.Threads.All(thread =>
            thread.ThreadId > 0));
    }

    private static WindowsProcessDetailsProvider CreateProvider(
        IWindowsProcessDetailsSource source,
        IProcessIdentityValidator? validator = null,
        RecordingLogger? logger = null)
    {
        return new WindowsProcessDetailsProvider(
            source,
            validator ??
            new StubIdentityValidator(Result.Success()),
            logger ?? new RecordingLogger());
    }

    private static IEnumerable<IWindowsModuleAccessor>
        EnumerateThenFail()
    {
        yield return new FakeModule();
        throw new Win32Exception(299);
    }

    private sealed class StubIdentityValidator(Result result) :
        IProcessIdentityValidator
    {
        public Result Validate(
            MonitoringSessionIdentity identity) => result;
    }

    private sealed class FakeSource(
        IReadOnlyList<IWindowsModuleAccessor>? modules = null,
        IReadOnlyList<IWindowsThreadAccessor>? threads = null,
        Func<IEnumerable<IWindowsModuleAccessor>>?
            modulesFactory = null) :
        IWindowsProcessDetailsSource
    {
        public int ModuleEnumerationCount { get; private set; }

        public int ThreadEnumerationCount { get; private set; }

        public IEnumerable<IWindowsModuleAccessor> EnumerateModules(
            int processId)
        {
            ModuleEnumerationCount++;
            return modulesFactory?.Invoke() ?? modules ?? [];
        }

        public IEnumerable<IWindowsThreadAccessor> EnumerateThreads(
            int processId)
        {
            ThreadEnumerationCount++;
            return threads ?? [];
        }
    }

    private sealed class FakeModule : IWindowsModuleAccessor
    {
        public Func<string> GetNameValue { get; init; } =
            () => "sample.dll";

        public Func<nint> GetBaseAddressValue { get; init; } =
            () => new IntPtr(0x140000000);

        public Func<int> GetSizeValue { get; init; } =
            () => 4096;

        public Func<string> GetPathValue { get; init; } =
            () => @"C:\sample.dll";

        public Func<string?> GetVersionValue { get; init; } =
            () => "1.2.3.4";

        public string GetName() => GetNameValue();

        public nint GetBaseAddress() =>
            GetBaseAddressValue();

        public int GetSize() => GetSizeValue();

        public string GetPath() => GetPathValue();

        public string? GetVersion() => GetVersionValue();
    }

    private sealed class FakeThread : IWindowsThreadAccessor
    {
        public Func<int> GetThreadIdValue { get; init; } =
            () => 123;

        public Func<string> GetStateValue { get; init; } =
            () => "Running";

        public Func<int> GetPriorityValue { get; init; } =
            () => 8;

        public Func<DateTimeOffset> GetStartTimeValue
        {
            get;
            init;
        } = () => new DateTimeOffset(
            2026,
            7,
            29,
            8,
            30,
            0,
            TimeSpan.Zero);

        public Func<TimeSpan> GetCpuTimeValue { get; init; } =
            () => TimeSpan.FromSeconds(2);

        public int GetThreadId() => GetThreadIdValue();

        public string GetState() => GetStateValue();

        public int GetPriority() => GetPriorityValue();

        public DateTimeOffset GetStartTime() =>
            GetStartTimeValue();

        public TimeSpan GetCpuTime() => GetCpuTimeValue();
    }
}
