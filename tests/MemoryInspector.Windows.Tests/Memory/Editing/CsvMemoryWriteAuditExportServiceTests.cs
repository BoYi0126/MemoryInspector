using MemoryInspector.Common;
using MemoryInspector.Core.Memory.Editing;
using MemoryInspector.Core.Monitoring;
using MemoryInspector.Core.Processes;
using MemoryInspector.Core.Scanning;
using MemoryInspector.Windows.Memory.Editing;
using MemoryInspector.Windows.Tests.Configuration;

namespace MemoryInspector.Windows.Tests.Memory.Editing;

[TestClass]
public sealed class CsvMemoryWriteAuditExportServiceTests
{
    [TestMethod]
    public async Task ExportWritesReadableFilteredAuditSummary()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var path = Path.Combine(
            temporaryDirectory.RootPath,
            "audit-summary.csv");
        var service = new CsvMemoryWriteAuditExportService();
        var entry = new MemoryWriteAuditEntry(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new MonitoringSessionIdentity(
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
                "MemoryInspector.TestTarget"),
            0x1000,
            ScanValueType.Int32,
            BitConverter.GetBytes(10),
            BitConverter.GetBytes(20),
            BitConverter.GetBytes(20),
            success: true,
            MemoryWriteVerificationStatus.Verified,
            MemoryWriteFailureReason.None,
            ErrorCode.None,
            errorMessage: null,
            new DateTimeOffset(
                2026,
                7,
                29,
                14,
                0,
                0,
                TimeSpan.Zero),
            MemoryWriteSource.ScanResult,
            "Counter, authorized test");

        var result = await service.ExportSummaryAsync(
            path,
            [entry]);
        var csv = await File.ReadAllTextAsync(path);

        Assert.IsTrue(result.IsSuccess);
        StringAssert.Contains(csv, "Time,Process,PID,Address");
        StringAssert.Contains(csv, "MemoryInspector.TestTarget");
        StringAssert.Contains(csv, "0x0000000000001000");
        StringAssert.Contains(
            csv,
            "\"Counter, authorized test\"");
    }
}
