using MemoryInspector.Common;
using MemoryInspector.Core.Memory.Editing;
using MemoryInspector.Core.Monitoring;
using MemoryInspector.Core.Processes;
using MemoryInspector.Core.Scanning;
using MemoryInspector.Windows.Configuration;
using MemoryInspector.Windows.Memory.Editing;
using MemoryInspector.Windows.Tests.Configuration;

namespace MemoryInspector.Windows.Tests.Memory.Editing;

[TestClass]
public sealed class JsonMemoryWriteAuditServiceTests
{
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
    public async Task SuccessAndFailureAttemptsRoundTripOutsideAppLogs()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var paths = new AppPathService(temporaryDirectory.RootPath);
        using var service =
            new JsonMemoryWriteAuditService(paths);
        var sessionId = Guid.NewGuid();
        var first = CreateEntry(
            sessionId,
            success: true,
            MemoryWriteFailureReason.None,
            ErrorCode.None,
            new DateTimeOffset(
                2026,
                7,
                29,
                12,
                0,
                0,
                TimeSpan.Zero));
        var second = CreateEntry(
            sessionId,
            success: false,
            MemoryWriteFailureReason.OriginalValueMismatch,
            ErrorCode.InvalidState,
            first.Timestamp.AddSeconds(1));

        var firstRecord = await service.RecordAsync(first);
        var secondRecord = await service.RecordAsync(second);
        var loaded = await service.ReadRecentAsync();

        Assert.IsTrue(firstRecord.IsSuccess);
        Assert.IsTrue(secondRecord.IsSuccess);
        Assert.IsTrue(loaded.IsSuccess);
        Assert.AreEqual(2, loaded.Value.Count);
        Assert.AreEqual(second.AuditId, loaded.Value[0].AuditId);
        Assert.AreEqual(first.AuditId, loaded.Value[1].AuditId);
        Assert.IsFalse(loaded.Value[0].Success);
        Assert.AreEqual(
            MemoryWriteFailureReason.OriginalValueMismatch,
            loaded.Value[0].FailureReason);
        Assert.AreEqual(
            "0A000000",
            Convert.ToHexString(
                loaded.Value[0].OriginalValue!.Value.Span));
        Assert.IsTrue(
            Directory.Exists(paths.MemoryEditorAuditDirectory));
        Assert.AreNotEqual(
            paths.LogsDirectory,
            paths.MemoryEditorAuditDirectory);
        Assert.AreEqual(
            0,
            Directory.EnumerateFiles(
                paths.MemoryEditorAuditDirectory,
                "*.tmp")
                .Count());
    }

    [TestMethod]
    public async Task CorruptAuditFileReturnsSerializationError()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var paths = new AppPathService(temporaryDirectory.RootPath);
        _ = paths.EnsureDirectories();
        await File.WriteAllTextAsync(
            Path.Combine(
                paths.MemoryEditorAuditDirectory,
                "9999999999999999999-corrupt.json"),
            "{not-json");
        using var service =
            new JsonMemoryWriteAuditService(paths);

        var result = await service.ReadRecentAsync();

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(
            ErrorCode.Serialization,
            result.Error.Code);
    }

    private static MemoryWriteAuditEntry CreateEntry(
        Guid sessionId,
        bool success,
        MemoryWriteFailureReason failureReason,
        ErrorCode errorCode,
        DateTimeOffset timestamp)
    {
        return new MemoryWriteAuditEntry(
            Guid.NewGuid(),
            sessionId,
            Identity,
            0x1000,
            ScanValueType.Int32,
            BitConverter.GetBytes(10),
            BitConverter.GetBytes(20),
            success
                ? new ReadOnlyMemory<byte>(
                    BitConverter.GetBytes(20))
                : default(ReadOnlyMemory<byte>?),
            success,
            success
                ? MemoryWriteVerificationStatus.Verified
                : MemoryWriteVerificationStatus.NotRequested,
            failureReason,
            errorCode,
            errorCode == ErrorCode.None
                ? null
                : "Original value mismatch.",
            timestamp,
            MemoryWriteSource.SavedAddress,
            "Audit test");
    }
}
