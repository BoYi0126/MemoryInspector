using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using MemoryInspector.Application.Configuration;
using MemoryInspector.Application.Scanning.Snapshots;
using MemoryInspector.Common;
using MemoryInspector.Core.Scanning;

namespace MemoryInspector.Windows.Scanning.Snapshots;

public sealed partial class BinarySnapshotStorage(
    IAppPathService pathService,
    TimeProvider timeProvider) : ISnapshotStorage, IDisposable
{
    public const int CurrentFormatVersion =
        SnapshotFormatInfo.CurrentVersion;
    internal const int HeaderSize =
        SnapshotFormatInfo.HeaderSize;
    private const int IoBufferSize = 64 * 1024;
    private const int IndexHeaderSize = 16;
    private const int IndexEntrySize = 64;
    private const int MaximumPageSize = 1_000_000;
    private const int IncludesValuesFlag = 1;
    private const string ProgressStage = "Writing snapshot";
    private static readonly byte[] SnapshotMagic =
        Encoding.ASCII.GetBytes("MISNAP18");
    private static readonly byte[] IndexMagic =
        Encoding.ASCII.GetBytes("MIINDEX1");
    private readonly IAppPathService _pathService =
        Guard.NotNull(pathService);
    private readonly TimeProvider _timeProvider =
        Guard.NotNull(timeProvider);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private bool _disposed;

    public async Task<Result<SnapshotDescriptor>> WriteAsync(
        SnapshotWriteRequest request,
        IAsyncEnumerable<SnapshotRecord> records,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return Validation<SnapshotDescriptor>(
                "A snapshot write request is required.");
        }

        if (records is null)
        {
            return Validation<SnapshotDescriptor>(
                "Snapshot records are required.");
        }

        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            await _writeGate.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
        {
            return Cancelled<SnapshotDescriptor>(
                "Snapshot writing was cancelled.",
                exception);
        }

        try
        {
            return await WriteCoreAsync(
                    request,
                    records,
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            return Failure<SnapshotDescriptor>(
                exception,
                "The snapshot could not be written.",
                cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<Result<SnapshotDescriptor>> OpenAsync(
        Guid sessionId,
        int nodeId,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateIdentity(sessionId, nodeId);

        if (validation.IsFailure)
        {
            return Result<SnapshotDescriptor>.Failure(
                validation.Error);
        }

        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            var path = GetExistingSnapshotPath(
                sessionId,
                nodeId);

            if (path is null)
            {
                return Result<SnapshotDescriptor>.Failure(
                    new Error(
                        ErrorCode.NotFound,
                        $"Snapshot node {nodeId} was not found."));
            }

            var descriptor = IsDeltaPath(path)
                ? await ValidateDeltaFileAsync(
                        path,
                        sessionId,
                        nodeId,
                        cancellationToken)
                    .ConfigureAwait(false)
                : await ValidateFileAsync(
                        path,
                        sessionId,
                        nodeId,
                        cancellationToken)
                    .ConfigureAwait(false);
            var referenceCount =
                await GetReferenceCountAsync(
                        sessionId,
                        nodeId,
                        cancellationToken)
                    .ConfigureAwait(false);
            return Result<SnapshotDescriptor>.Success(
                WithReferenceCount(
                    descriptor,
                    referenceCount));
        }
        catch (Exception exception)
        {
            return Failure<SnapshotDescriptor>(
                exception,
                "The snapshot could not be opened.",
                cancellationToken);
        }
    }

    public async Task<Result<PagedResult<SnapshotRecord>>> ReadPageAsync(
        SnapshotDescriptor snapshot,
        long pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (snapshot is null)
        {
            return Validation<PagedResult<SnapshotRecord>>(
                "A snapshot descriptor is required.");
        }

        if (pageNumber <= 0)
        {
            return Validation<PagedResult<SnapshotRecord>>(
                "Page number must be greater than zero.");
        }

        if (pageSize <= 0 || pageSize > MaximumPageSize)
        {
            return Validation<PagedResult<SnapshotRecord>>(
                $"Page size must be between 1 and " +
                $"{MaximumPageSize:N0}.");
        }

        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            if (snapshot.StorageKind !=
                SnapshotStorageKind.Full)
            {
                return await ReadDeltaPageAsync(
                        snapshot,
                        pageNumber,
                        pageSize,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            var expectedPath = GetSnapshotPath(
                snapshot.SessionId,
                snapshot.NodeId);

            if (!Path.GetFullPath(snapshot.FilePath).Equals(
                    Path.GetFullPath(expectedPath),
                    StringComparison.OrdinalIgnoreCase))
            {
                return Validation<PagedResult<SnapshotRecord>>(
                    "Snapshot path is outside the expected session node.");
            }

            await using var stream = OpenRead(expectedPath);
            var header = await ReadHeaderAsync(
                    stream,
                    cancellationToken)
                .ConfigureAwait(false);
            ValidateDescriptor(snapshot, header, expectedPath);

            var totalPages = CalculateTotalPages(
                header.RecordCount,
                pageSize);

            if ((totalPages == 0 && pageNumber != 1) ||
                (totalPages > 0 && pageNumber > totalPages))
            {
                return Validation<PagedResult<SnapshotRecord>>(
                    "Page number exceeds the snapshot page count.");
            }

            var startRecord = checked(
                (pageNumber - 1) * (long)pageSize);
            var itemCount = header.RecordCount == 0
                ? 0
                : (int)Math.Min(
                    pageSize,
                    header.RecordCount - startRecord);
            var page = new SnapshotRecord[itemCount];
            stream.Position = checked(
                HeaderSize +
                startRecord * header.RecordSize);
            var recordBuffer = new byte[header.RecordSize];

            for (var index = 0; index < itemCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await stream.ReadExactlyAsync(
                        recordBuffer,
                        cancellationToken)
                    .ConfigureAwait(false);
                var address = BinaryPrimitives
                    .ReadUInt64LittleEndian(recordBuffer);
                var value = header.ValueSize == 0
                    ? ReadOnlyMemory<byte>.Empty
                    : recordBuffer
                        .AsSpan(sizeof(ulong), header.ValueSize)
                        .ToArray();
                page[index] = new SnapshotRecord(
                    new CandidateAddress(address),
                    value);
            }

            return Result<PagedResult<SnapshotRecord>>.Success(
                new PagedResult<SnapshotRecord>(
                    page,
                    pageNumber,
                    pageSize,
                    header.RecordCount));
        }
        catch (Exception exception)
        {
            return Failure<PagedResult<SnapshotRecord>>(
                exception,
                "The snapshot page could not be read.",
                cancellationToken);
        }
    }

    public async Task<Result<SnapshotRecoveryResult>>
        RecoverIncompleteAsync(
            Guid sessionId,
            CancellationToken cancellationToken = default)
    {
        if (sessionId == Guid.Empty)
        {
            return Validation<SnapshotRecoveryResult>(
                "Session ID cannot be empty.");
        }

        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            await _writeGate.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
        {
            return Cancelled<SnapshotRecoveryResult>(
                "Snapshot recovery was cancelled.",
                exception);
        }

        try
        {
            var sessionDirectory = GetSessionDirectory(sessionId);

            if (!Directory.Exists(sessionDirectory))
            {
                return Result<SnapshotRecoveryResult>.Success(
                    new SnapshotRecoveryResult(0, 0));
            }

            var recovered = 0;
            var discarded = 0;
            var temporaryFiles = Directory
                .EnumerateFiles(
                    sessionDirectory,
                    "node_*.full.bin.tmp-*",
                    SearchOption.TopDirectoryOnly)
                .ToArray();

            foreach (var temporaryFile in temporaryFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var descriptor = await ValidateFileAsync(
                            temporaryFile,
                            sessionId,
                            expectedNodeId: null,
                            cancellationToken)
                        .ConfigureAwait(false);
                    var finalPath = GetSnapshotPath(
                        sessionId,
                        descriptor.NodeId);

                    if (File.Exists(finalPath))
                    {
                        File.Delete(temporaryFile);
                        discarded++;
                        continue;
                    }

                    File.Move(temporaryFile, finalPath);
                    recovered++;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception) when (
                    IsRecoverableFileException(exception))
                {
                    File.Delete(temporaryFile);
                    discarded++;
                }
            }

            await RebuildIndexAsync(
                    sessionId,
                    CancellationToken.None)
                .ConfigureAwait(false);
            await RebuildReferenceIndexAsync(
                    sessionId,
                    CancellationToken.None)
                .ConfigureAwait(false);

            return Result<SnapshotRecoveryResult>.Success(
                new SnapshotRecoveryResult(
                    recovered,
                    discarded));
        }
        catch (Exception exception)
        {
            return Failure<SnapshotRecoveryResult>(
                exception,
                "Incomplete snapshot recovery failed.",
                cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<Result> DeleteAsync(
        Guid sessionId,
        int nodeId,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateIdentity(sessionId, nodeId);

        if (validation.IsFailure)
        {
            return validation;
        }

        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            await _writeGate.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
        {
            return Result.Failure(
                new Error(
                    ErrorCode.Cancelled,
                    "Snapshot deletion was cancelled.",
                    exception));
        }

        try
        {
            var path = GetExistingSnapshotPath(
                sessionId,
                nodeId);

            if (path is null)
            {
                return Result.Failure(
                    new Error(
                        ErrorCode.NotFound,
                        $"Snapshot node {nodeId} was not found."));
            }

            var referenceCount =
                await GetReferenceCountAsync(
                        sessionId,
                        nodeId,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (referenceCount > 0)
            {
                return Result.Failure(
                    new Error(
                        ErrorCode.InvalidState,
                        $"Snapshot node {nodeId} is referenced by " +
                        $"{referenceCount} delta snapshot(s)."));
            }

            File.Delete(path);
            await RebuildIndexAsync(
                    sessionId,
                    CancellationToken.None)
                .ConfigureAwait(false);
            await RebuildReferenceIndexAsync(
                    sessionId,
                    CancellationToken.None)
                .ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception exception)
        {
            var failure = Failure<object>(
                exception,
                "The snapshot could not be deleted.",
                cancellationToken);
            return Result.Failure(failure.Error);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _writeGate.Dispose();
        _disposed = true;
    }

    private async Task<Result<SnapshotDescriptor>> WriteCoreAsync(
        SnapshotWriteRequest request,
        IAsyncEnumerable<SnapshotRecord> records,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        var directoryResult = _pathService.EnsureDirectories();

        if (directoryResult.IsFailure)
        {
            return Result<SnapshotDescriptor>.Failure(
                directoryResult.Error);
        }

        var sessionDirectory = GetSessionDirectory(
            request.SessionId);
        Directory.CreateDirectory(sessionDirectory);
        var existingPath = GetExistingSnapshotPath(
            request.SessionId,
            request.NodeId);

        if (existingPath is not null)
        {
            var referenceCount = await GetReferenceCountAsync(
                    request.SessionId,
                    request.NodeId,
                    cancellationToken)
                .ConfigureAwait(false);

            if (IsDeltaPath(existingPath) ||
                referenceCount > 0)
            {
                throw new SnapshotValidationException(
                    "A delta snapshot or referenced snapshot " +
                    "node cannot be overwritten.");
            }
        }

        var finalPath = GetSnapshotPath(
            request.SessionId,
            request.NodeId);
        var temporaryPath =
            $"{finalPath}.tmp-{Guid.NewGuid():N}";
        var committed = false;

        try
        {
            var createdAt = _timeProvider.GetUtcNow();
            var initialHeader = SnapshotHeader.Create(
                request,
                createdAt);
            long recordCount = 0;
            long payloadLength = 0;
            var writeBufferLength = Math.Max(
                request.RecordSize,
                IoBufferSize -
                (IoBufferSize % request.RecordSize));
            var writeBuffer = new byte[writeBufferLength];
            var bufferedBytes = 0;
            using var checksum = IncrementalHash.CreateHash(
                HashAlgorithmName.SHA256);

            progress?.Report(
                new OperationProgress(
                    0,
                    request.ExpectedRecordCount,
                    ProgressStage));

            await using (var stream = OpenWrite(temporaryPath))
            {
                await WriteHeaderAsync(
                        stream,
                        initialHeader,
                        cancellationToken)
                    .ConfigureAwait(false);

                await foreach (var record in records
                    .WithCancellation(cancellationToken)
                    .ConfigureAwait(false))
                {
                    ValidateRecord(record, request);

                    if (bufferedBytes + request.RecordSize >
                        writeBuffer.Length)
                    {
                        await FlushPayloadBufferAsync(
                                stream,
                                writeBuffer,
                                bufferedBytes,
                                checksum,
                                cancellationToken)
                            .ConfigureAwait(false);
                        bufferedBytes = 0;
                    }

                    var destination = writeBuffer.AsSpan(
                        bufferedBytes,
                        request.RecordSize);
                    BinaryPrimitives.WriteUInt64LittleEndian(
                        destination,
                        record.Candidate.Address);

                    if (request.IncludeValues)
                    {
                        record.Value.Span.CopyTo(
                            destination[sizeof(ulong)..]);
                    }

                    bufferedBytes += request.RecordSize;
                    recordCount = checked(recordCount + 1);
                    payloadLength = checked(
                        payloadLength + request.RecordSize);

                    if (recordCount % 4096 == 0)
                    {
                        progress?.Report(
                            new OperationProgress(
                                GetProgressCompleted(
                                    recordCount,
                                    request.ExpectedRecordCount),
                                request.ExpectedRecordCount,
                                ProgressStage));
                    }
                }

                if (bufferedBytes > 0)
                {
                    await FlushPayloadBufferAsync(
                            stream,
                            writeBuffer,
                            bufferedBytes,
                            checksum,
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                if (request.ExpectedRecordCount.HasValue &&
                    request.ExpectedRecordCount.Value != recordCount)
                {
                    throw new SnapshotValidationException(
                        $"Expected {request.ExpectedRecordCount.Value:N0} " +
                        $"records but received {recordCount:N0}.");
                }

                var finalHeader = initialHeader with
                {
                    RecordCount = recordCount,
                    PayloadLength = payloadLength,
                    Checksum = checksum.GetHashAndReset(),
                };
                await WriteHeaderAsync(
                        stream,
                        finalHeader,
                        cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken)
                    .ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(
                temporaryPath,
                finalPath,
                overwrite: true);
            committed = true;
            await RebuildIndexAsync(
                    request.SessionId,
                    CancellationToken.None)
                .ConfigureAwait(false);
            await RebuildReferenceIndexAsync(
                    request.SessionId,
                    CancellationToken.None)
                .ConfigureAwait(false);
            var descriptor = await ValidateFileAsync(
                    finalPath,
                    request.SessionId,
                    request.NodeId,
                    CancellationToken.None)
                .ConfigureAwait(false);

            progress?.Report(
                new OperationProgress(
                    recordCount,
                    request.ExpectedRecordCount,
                    ProgressStage));

            return Result<SnapshotDescriptor>.Success(
                descriptor);
        }
        finally
        {
            if (!committed && File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private async Task<SnapshotDescriptor> ValidateFileAsync(
        string path,
        Guid expectedSessionId,
        int? expectedNodeId,
        CancellationToken cancellationToken)
    {
        await using var stream = OpenRead(path);
        var header = await ReadHeaderAsync(
                stream,
                cancellationToken)
            .ConfigureAwait(false);

        if (header.SessionId != expectedSessionId ||
            (expectedNodeId.HasValue &&
             header.NodeId != expectedNodeId.Value))
        {
            throw new InvalidDataException(
                "Snapshot identity does not match its path.");
        }

        if (stream.Length !=
            checked(HeaderSize + header.PayloadLength))
        {
            throw new InvalidDataException(
                "Snapshot file length does not match its header.");
        }

        stream.Position = HeaderSize;
        using var checksum = IncrementalHash.CreateHash(
            HashAlgorithmName.SHA256);
        var buffer = new byte[IoBufferSize];
        long remaining = header.PayloadLength;

        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var readLength = (int)Math.Min(
                buffer.Length,
                remaining);
            var bytesRead = await stream.ReadAsync(
                    buffer.AsMemory(0, readLength),
                    cancellationToken)
                .ConfigureAwait(false);

            if (bytesRead == 0)
            {
                throw new EndOfStreamException(
                    "Snapshot payload ended before its declared length.");
            }

            checksum.AppendData(
                buffer.AsSpan(0, bytesRead));
            remaining -= bytesRead;
        }

        var actualChecksum = checksum.GetHashAndReset();

        if (!CryptographicOperations.FixedTimeEquals(
            actualChecksum,
            header.Checksum))
        {
            throw new InvalidDataException(
                "Snapshot checksum validation failed.");
        }

        return CreateDescriptor(header, path);
    }

    private async Task RebuildIndexAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var sessionDirectory = GetSessionDirectory(sessionId);
        Directory.CreateDirectory(sessionDirectory);
        var headers = new List<SnapshotHeader>();

        foreach (var path in Directory
            .EnumerateFiles(
                sessionDirectory,
                "node_*.full.bin",
                SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var stream = OpenRead(path);
                var header = await ReadHeaderAsync(
                        stream,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (header.SessionId == sessionId)
                {
                    headers.Add(header);
                }
            }
            catch (Exception exception) when (
                exception is
                    InvalidDataException or
                    EndOfStreamException or
                    OverflowException)
            {
                continue;
            }
        }

        headers.Sort((left, right) =>
            left.NodeId.CompareTo(right.NodeId));
        var finalPath = Path.Combine(
            sessionDirectory,
            "index.bin");
        var temporaryPath =
            $"{finalPath}.tmp-{Guid.NewGuid():N}";
        var committed = false;

        try
        {
            await using (var stream = OpenWrite(temporaryPath))
            {
                var indexHeader = new byte[IndexHeaderSize];
                IndexMagic.CopyTo(indexHeader, 0);
                BinaryPrimitives.WriteInt32LittleEndian(
                    indexHeader.AsSpan(8),
                    CurrentFormatVersion);
                BinaryPrimitives.WriteInt32LittleEndian(
                    indexHeader.AsSpan(12),
                    headers.Count);
                await stream.WriteAsync(
                        indexHeader,
                        cancellationToken)
                    .ConfigureAwait(false);

                foreach (var header in headers)
                {
                    var entry = new byte[IndexEntrySize];
                    BinaryPrimitives.WriteInt32LittleEndian(
                        entry,
                        header.NodeId);
                    BinaryPrimitives.WriteInt32LittleEndian(
                        entry.AsSpan(4),
                        (int)header.ValueType);
                    BinaryPrimitives.WriteInt32LittleEndian(
                        entry.AsSpan(8),
                        header.IncludesValues
                            ? IncludesValuesFlag
                            : 0);
                    BinaryPrimitives.WriteInt32LittleEndian(
                        entry.AsSpan(12),
                        header.ValueSize);
                    BinaryPrimitives.WriteInt64LittleEndian(
                        entry.AsSpan(16),
                        header.RecordCount);
                    BinaryPrimitives.WriteInt64LittleEndian(
                        entry.AsSpan(24),
                        header.PayloadLength);
                    header.Checksum.CopyTo(
                        entry,
                        32);
                    await stream.WriteAsync(
                            entry,
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                await stream.FlushAsync(cancellationToken)
                    .ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            File.Move(
                temporaryPath,
                finalPath,
                overwrite: true);
            committed = true;
        }
        finally
        {
            if (!committed && File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static async Task FlushPayloadBufferAsync(
        FileStream stream,
        byte[] buffer,
        int length,
        IncrementalHash checksum,
        CancellationToken cancellationToken)
    {
        checksum.AppendData(buffer.AsSpan(0, length));
        await stream.WriteAsync(
                buffer.AsMemory(0, length),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static void ValidateRecord(
        SnapshotRecord record,
        SnapshotWriteRequest request)
    {
        if (record.Value.Length != request.ValueSize)
        {
            throw new SnapshotValidationException(
                request.IncludeValues
                    ? $"Every snapshot value must contain exactly " +
                      $"{request.ValueSize} bytes."
                    : "Address-only snapshots cannot contain values.");
        }
    }

    private static async Task WriteHeaderAsync(
        FileStream stream,
        SnapshotHeader header,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[HeaderSize];
        SnapshotMagic.CopyTo(buffer, 0);
        BinaryPrimitives.WriteInt32LittleEndian(
            buffer.AsSpan(8),
            CurrentFormatVersion);
        BinaryPrimitives.WriteInt32LittleEndian(
            buffer.AsSpan(12),
            HeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(
            buffer.AsSpan(16),
            header.IncludesValues
                ? IncludesValuesFlag
                : 0);
        BinaryPrimitives.WriteInt32LittleEndian(
            buffer.AsSpan(20),
            (int)header.ValueType);
        BinaryPrimitives.WriteInt32LittleEndian(
            buffer.AsSpan(24),
            header.ValueSize);
        BinaryPrimitives.WriteInt32LittleEndian(
            buffer.AsSpan(28),
            header.RecordSize);
        BinaryPrimitives.WriteInt64LittleEndian(
            buffer.AsSpan(32),
            header.RecordCount);
        BinaryPrimitives.WriteInt64LittleEndian(
            buffer.AsSpan(40),
            header.PayloadLength);
        header.SessionId.TryWriteBytes(
            buffer.AsSpan(48, 16));
        BinaryPrimitives.WriteInt32LittleEndian(
            buffer.AsSpan(64),
            header.NodeId);
        BinaryPrimitives.WriteInt64LittleEndian(
            buffer.AsSpan(72),
            header.CreatedAt.ToUnixTimeMilliseconds());
        header.Checksum.CopyTo(buffer, 80);
        stream.Position = 0;
        await stream.WriteAsync(
                buffer,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<SnapshotHeader> ReadHeaderAsync(
        FileStream stream,
        CancellationToken cancellationToken)
    {
        if (stream.Length < HeaderSize)
        {
            throw new InvalidDataException(
                "Snapshot header is incomplete.");
        }

        var buffer = new byte[HeaderSize];
        stream.Position = 0;
        await stream.ReadExactlyAsync(
                buffer,
                cancellationToken)
            .ConfigureAwait(false);

        if (!buffer.AsSpan(0, 8).SequenceEqual(SnapshotMagic))
        {
            throw new InvalidDataException(
                "Snapshot magic is invalid.");
        }

        var version = BinaryPrimitives.ReadInt32LittleEndian(
            buffer.AsSpan(8));
        var headerSize = BinaryPrimitives.ReadInt32LittleEndian(
            buffer.AsSpan(12));

        if (version != CurrentFormatVersion ||
            headerSize != HeaderSize)
        {
            throw new InvalidDataException(
                $"Unsupported snapshot format version {version}.");
        }

        var flags = BinaryPrimitives.ReadInt32LittleEndian(
            buffer.AsSpan(16));
        var rawValueType = BinaryPrimitives.ReadInt32LittleEndian(
            buffer.AsSpan(20));

        if ((flags & ~IncludesValuesFlag) != 0 ||
            !Enum.IsDefined(
                typeof(ScanValueType),
                rawValueType))
        {
            throw new InvalidDataException(
                "Snapshot value layout is invalid.");
        }

        var valueType = (ScanValueType)rawValueType;
        var includesValues =
            (flags & IncludesValuesFlag) != 0;
        var valueSize = BinaryPrimitives.ReadInt32LittleEndian(
            buffer.AsSpan(24));
        var recordSize = BinaryPrimitives.ReadInt32LittleEndian(
            buffer.AsSpan(28));
        var recordCount = BinaryPrimitives.ReadInt64LittleEndian(
            buffer.AsSpan(32));
        var payloadLength = BinaryPrimitives.ReadInt64LittleEndian(
            buffer.AsSpan(40));
        var sessionId = new Guid(
            buffer.AsSpan(48, 16));
        var nodeId = BinaryPrimitives.ReadInt32LittleEndian(
            buffer.AsSpan(64));
        var createdAtMilliseconds =
            BinaryPrimitives.ReadInt64LittleEndian(
                buffer.AsSpan(72));
        var checksum = buffer
            .AsSpan(80, SHA256.HashSizeInBytes)
            .ToArray();
        var expectedValueSize = includesValues
            ? ScanValueTypeInfo.GetSize(valueType)
            : 0;

        long expectedPayloadLength;

        try
        {
            expectedPayloadLength = checked(
                recordCount * recordSize);
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException(
                "Snapshot payload length overflows the format.",
                exception);
        }

        if (sessionId == Guid.Empty ||
            nodeId <= 0 ||
            valueSize != expectedValueSize ||
            recordSize != sizeof(ulong) + valueSize ||
            recordCount < 0 ||
            payloadLength < 0 ||
            expectedPayloadLength != payloadLength)
        {
            throw new InvalidDataException(
                "Snapshot header metadata is inconsistent.");
        }

        DateTimeOffset createdAt;

        try
        {
            createdAt = DateTimeOffset.FromUnixTimeMilliseconds(
                createdAtMilliseconds);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InvalidDataException(
                "Snapshot creation timestamp is invalid.",
                exception);
        }

        return new SnapshotHeader(
            sessionId,
            nodeId,
            valueType,
            includesValues,
            valueSize,
            recordSize,
            recordCount,
            payloadLength,
            checksum,
            createdAt);
    }

    private static void ValidateDescriptor(
        SnapshotDescriptor descriptor,
        SnapshotHeader header,
        string expectedPath)
    {
        if (descriptor.SessionId != header.SessionId ||
            descriptor.NodeId != header.NodeId ||
            descriptor.FormatVersion != CurrentFormatVersion ||
            descriptor.ValueType != header.ValueType ||
            descriptor.IncludesValues != header.IncludesValues ||
            descriptor.ValueSize != header.ValueSize ||
            descriptor.RecordSize != header.RecordSize ||
            descriptor.RecordCount != header.RecordCount ||
            descriptor.PayloadLength != header.PayloadLength ||
            descriptor.StorageKind != SnapshotStorageKind.Full ||
            !descriptor.Checksum.Equals(
                Convert.ToHexString(header.Checksum),
                StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFullPath(descriptor.FilePath).Equals(
                Path.GetFullPath(expectedPath),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Snapshot descriptor does not match the file header.");
        }
    }

    private static SnapshotDescriptor CreateDescriptor(
        SnapshotHeader header,
        string path)
    {
        return new SnapshotDescriptor(
            header.SessionId,
            header.NodeId,
            CurrentFormatVersion,
            header.ValueType,
            header.IncludesValues,
            header.ValueSize,
            header.RecordSize,
            header.RecordCount,
            header.PayloadLength,
            Convert.ToHexString(header.Checksum),
            header.CreatedAt,
            Path.GetFullPath(path));
    }

    private string GetSessionDirectory(Guid sessionId)
    {
        return Path.Combine(
            _pathService.TempDirectory,
            sessionId.ToString("D"));
    }

    private string GetSnapshotPath(
        Guid sessionId,
        int nodeId)
    {
        return Path.Combine(
            GetSessionDirectory(sessionId),
            $"node_{nodeId:D4}.full.bin");
    }

    private static FileStream OpenRead(string path)
    {
        return new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read | FileShare.Delete,
            IoBufferSize,
            FileOptions.Asynchronous |
            FileOptions.SequentialScan);
    }

    private static FileStream OpenWrite(string path)
    {
        return new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            IoBufferSize,
            FileOptions.Asynchronous |
            FileOptions.SequentialScan);
    }

    private static Result ValidateIdentity(
        Guid sessionId,
        int nodeId)
    {
        if (sessionId == Guid.Empty)
        {
            return Result.Failure(
                new Error(
                    ErrorCode.Validation,
                    "Session ID cannot be empty."));
        }

        return nodeId > 0
            ? Result.Success()
            : Result.Failure(
                new Error(
                    ErrorCode.Validation,
                    "Node ID must be greater than zero."));
    }

    private static long CalculateTotalPages(
        long totalCount,
        int pageSize)
    {
        return totalCount / pageSize +
               (totalCount % pageSize == 0 ? 0 : 1);
    }

    private static long GetProgressCompleted(
        long recordCount,
        long? expectedRecordCount)
    {
        return expectedRecordCount.HasValue
            ? Math.Min(recordCount, expectedRecordCount.Value)
            : recordCount;
    }

    private static bool IsRecoverableFileException(
        Exception exception)
    {
        return exception is
            InvalidDataException or
            EndOfStreamException or
            IOException or
            UnauthorizedAccessException or
            CryptographicException or
            OverflowException;
    }

    private static Result<T> Failure<T>(
        Exception exception,
        string message,
        CancellationToken cancellationToken)
    {
        return exception switch
        {
            OperationCanceledException
                when cancellationToken.IsCancellationRequested =>
                Cancelled<T>(message, exception),
            SnapshotValidationException =>
                Result<T>.Failure(
                    new Error(
                        ErrorCode.Validation,
                        exception.Message,
                        exception)),
            InvalidDataException or
            EndOfStreamException or
            CryptographicException =>
                Result<T>.Failure(
                    new Error(
                        ErrorCode.Serialization,
                        message,
                        exception)),
            FileNotFoundException or
            DirectoryNotFoundException =>
                Result<T>.Failure(
                    new Error(
                        ErrorCode.NotFound,
                        message,
                        exception)),
            IOException or
            UnauthorizedAccessException or
            NotSupportedException or
            OverflowException or
            OutOfMemoryException =>
                Result<T>.Failure(
                    new Error(
                        SnapshotStorageErrorClassifier.Classify(
                            exception),
                        message,
                        exception)),
            _ => Result<T>.Failure(
                new Error(
                    ErrorCode.Unexpected,
                    message,
                    exception)),
        };
    }

    private static Result<T> Validation<T>(string message)
    {
        return Result<T>.Failure(
            new Error(
                ErrorCode.Validation,
                message));
    }

    private static Result<T> Cancelled<T>(
        string message,
        Exception? exception = null)
    {
        return Result<T>.Failure(
            new Error(
                ErrorCode.Cancelled,
                message,
                exception));
    }

    private sealed record SnapshotHeader(
        Guid SessionId,
        int NodeId,
        ScanValueType ValueType,
        bool IncludesValues,
        int ValueSize,
        int RecordSize,
        long RecordCount,
        long PayloadLength,
        byte[] Checksum,
        DateTimeOffset CreatedAt)
    {
        public static SnapshotHeader Create(
            SnapshotWriteRequest request,
            DateTimeOffset createdAt)
        {
            return new SnapshotHeader(
                request.SessionId,
                request.NodeId,
                request.ValueType,
                request.IncludeValues,
                request.ValueSize,
                request.RecordSize,
                0,
                0,
                new byte[SHA256.HashSizeInBytes],
                createdAt);
        }
    }

    private sealed class SnapshotValidationException(
        string message) : Exception(message);
}
