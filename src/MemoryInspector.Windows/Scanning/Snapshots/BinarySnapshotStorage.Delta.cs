using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MemoryInspector.Application.Scanning.Snapshots;
using MemoryInspector.Common;
using MemoryInspector.Core.Scanning;

namespace MemoryInspector.Windows.Scanning.Snapshots;

public sealed partial class BinarySnapshotStorage
{
    internal const int DeltaHeaderSize = 160;
    internal const int MaximumDeltaChainDepth = 4;
    private const int DeltaFormatVersion = 1;
    private const string ReferenceIndexFileName =
        "references.json";
    private static readonly byte[] DeltaMagic =
        Encoding.ASCII.GetBytes("MIDELT19");

    public async Task<Result<SnapshotDescriptor>> OptimizeAsync(
        SnapshotDescriptor parentSnapshot,
        SnapshotDescriptor fullSnapshot,
        CancellationToken cancellationToken = default)
    {
        if (parentSnapshot is null || fullSnapshot is null)
        {
            return Validation<SnapshotDescriptor>(
                "Parent and child snapshots are required.");
        }

        if (parentSnapshot.SessionId != fullSnapshot.SessionId ||
            parentSnapshot.NodeId == fullSnapshot.NodeId ||
            parentSnapshot.ValueType != fullSnapshot.ValueType ||
            parentSnapshot.IncludesValues !=
                fullSnapshot.IncludesValues ||
            fullSnapshot.StorageKind != SnapshotStorageKind.Full)
        {
            return Validation<SnapshotDescriptor>(
                "Only a compatible full child snapshot can be optimized.");
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
                "Snapshot optimization was cancelled.",
                exception);
        }

        try
        {
            if (parentSnapshot.ChainDepth >=
                    MaximumDeltaChainDepth ||
                parentSnapshot.RecordCount == 0)
            {
                return Result<SnapshotDescriptor>.Success(
                    fullSnapshot);
            }

            return await OptimizeCoreAsync(
                    parentSnapshot,
                    fullSnapshot,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            return Failure<SnapshotDescriptor>(
                exception,
                "The snapshot could not be delta optimized.",
                cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task<Result<SnapshotDescriptor>>
        OptimizeCoreAsync(
            SnapshotDescriptor parentSnapshot,
            SnapshotDescriptor fullSnapshot,
            CancellationToken cancellationToken)
    {
        var sessionDirectory = GetSessionDirectory(
            fullSnapshot.SessionId);
        Directory.CreateDirectory(sessionDirectory);
        var token = Guid.NewGuid().ToString("N");
        var keepRawPath = Path.Combine(
            sessionDirectory,
            $".delta-keep-{token}.tmp");
        var removeRawPath = Path.Combine(
            sessionDirectory,
            $".delta-remove-{token}.tmp");
        var updateRawPath = Path.Combine(
            sessionDirectory,
            $".delta-update-{token}.tmp");
        string? deltaTemporaryPath = null;
        string? deltaFinalPath = null;
        var deltaMoved = false;
        var originalFullRemoved = false;

        try
        {
            long keepCount = 0;
            long removeCount = 0;
            long updateCount = 0;

            await using (var keepStream = OpenRawWrite(keepRawPath))
            await using (var removeStream = OpenRawWrite(removeRawPath))
            await using (var updateStream = OpenRawWrite(updateRawPath))
            await using (var parent = EnumerateRecordsAsync(
                    parentSnapshot,
                    cancellationToken)
                .GetAsyncEnumerator(cancellationToken))
            await using (var child = EnumerateRecordsAsync(
                    fullSnapshot,
                    cancellationToken)
                .GetAsyncEnumerator(cancellationToken))
            {
                var hasParent = await parent.MoveNextAsync()
                    .ConfigureAwait(false);
                var hasChild = await child.MoveNextAsync()
                    .ConfigureAwait(false);

                while (hasParent)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!hasChild ||
                        parent.Current.Candidate.Address <
                        child.Current.Candidate.Address)
                    {
                        await WriteAddressAsync(
                                removeStream,
                                parent.Current.Candidate.Address,
                                cancellationToken)
                            .ConfigureAwait(false);
                        removeCount++;
                        hasParent = await parent.MoveNextAsync()
                            .ConfigureAwait(false);
                        continue;
                    }

                    if (child.Current.Candidate.Address <
                        parent.Current.Candidate.Address)
                    {
                        throw new InvalidDataException(
                            "A delta child contains an address " +
                            "that is absent from its parent.");
                    }

                    await WriteRecordAsync(
                            keepStream,
                            child.Current,
                            fullSnapshot.ValueSize,
                            cancellationToken)
                        .ConfigureAwait(false);
                    keepCount++;

                    if (!child.Current.Value.Span.SequenceEqual(
                        parent.Current.Value.Span))
                    {
                        await WriteRecordAsync(
                                updateStream,
                                child.Current,
                                fullSnapshot.ValueSize,
                                cancellationToken)
                            .ConfigureAwait(false);
                        updateCount++;
                    }

                    hasParent = await parent.MoveNextAsync()
                        .ConfigureAwait(false);
                    hasChild = await child.MoveNextAsync()
                        .ConfigureAwait(false);
                }

                if (hasChild)
                {
                    throw new InvalidDataException(
                        "A delta child is not a subset of its parent.");
                }

                await keepStream.FlushAsync(cancellationToken)
                    .ConfigureAwait(false);
                await removeStream.FlushAsync(cancellationToken)
                    .ConfigureAwait(false);
                await updateStream.FlushAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            if (keepCount != fullSnapshot.RecordCount ||
                keepCount + removeCount !=
                    parentSnapshot.RecordCount)
            {
                throw new InvalidDataException(
                    "Delta candidate counts are inconsistent.");
            }

            var keepBytes = new FileInfo(keepRawPath).Length;
            var removeBytes = checked(
                new FileInfo(removeRawPath).Length +
                new FileInfo(updateRawPath).Length);
            var kind = keepBytes < removeBytes
                ? SnapshotStorageKind.DeltaKeep
                : SnapshotStorageKind.DeltaRemove;
            var selectedBytes = kind ==
                SnapshotStorageKind.DeltaKeep
                ? keepBytes
                : removeBytes;
            var accumulatedBytes = checked(
                parentSnapshot.AccumulatedDeltaBytes +
                selectedBytes);
            var maximumDeltaBytes =
                parentSnapshot.FullPayloadLength / 2;

            if (selectedBytes > maximumDeltaBytes ||
                accumulatedBytes > maximumDeltaBytes)
            {
                return Result<SnapshotDescriptor>.Success(
                    fullSnapshot);
            }

            deltaFinalPath = GetDeltaSnapshotPath(
                fullSnapshot.SessionId,
                fullSnapshot.NodeId,
                kind);
            deltaTemporaryPath =
                $"{deltaFinalPath}.tmp-{Guid.NewGuid():N}";
            var header = new DeltaHeader(
                kind,
                fullSnapshot.SessionId,
                fullSnapshot.NodeId,
                parentSnapshot.NodeId,
                fullSnapshot.ValueType,
                fullSnapshot.IncludesValues,
                fullSnapshot.ValueSize,
                fullSnapshot.RecordSize,
                fullSnapshot.RecordCount,
                selectedBytes,
                parentSnapshot.ChainDepth + 1,
                accumulatedBytes,
                kind == SnapshotStorageKind.DeltaRemove
                    ? removeCount
                    : 0,
                kind == SnapshotStorageKind.DeltaRemove
                    ? updateCount
                    : 0,
                fullSnapshot.CreatedAt,
                new byte[SHA256.HashSizeInBytes]);

            await WriteDeltaFileAsync(
                    deltaTemporaryPath,
                    header,
                    kind == SnapshotStorageKind.DeltaKeep
                        ? [keepRawPath]
                        : [removeRawPath, updateRawPath],
                    cancellationToken)
                .ConfigureAwait(false);
            File.Move(
                deltaTemporaryPath,
                deltaFinalPath,
                overwrite: false);
            deltaMoved = true;
            deltaTemporaryPath = null;
            var descriptor = await ValidateDeltaFileAsync(
                    deltaFinalPath,
                    fullSnapshot.SessionId,
                    fullSnapshot.NodeId,
                    CancellationToken.None)
                .ConfigureAwait(false);
            File.Delete(fullSnapshot.FilePath);
            originalFullRemoved = true;
            await RebuildIndexAsync(
                    fullSnapshot.SessionId,
                    CancellationToken.None)
                .ConfigureAwait(false);
            await RebuildReferenceIndexAsync(
                    fullSnapshot.SessionId,
                    CancellationToken.None)
                .ConfigureAwait(false);
            return Result<SnapshotDescriptor>.Success(
                descriptor);
        }
        finally
        {
            DeleteIfExists(keepRawPath);
            DeleteIfExists(removeRawPath);
            DeleteIfExists(updateRawPath);

            if (deltaTemporaryPath is not null)
            {
                DeleteIfExists(deltaTemporaryPath);
            }

            if (!originalFullRemoved &&
                deltaMoved &&
                deltaFinalPath is not null)
            {
                DeleteIfExists(deltaFinalPath);
            }
        }
    }

    private async Task<Result<PagedResult<SnapshotRecord>>>
        ReadDeltaPageAsync(
            SnapshotDescriptor snapshot,
            long pageNumber,
            int pageSize,
            CancellationToken cancellationToken)
    {
        var validated = await ValidateDeltaFileAsync(
                snapshot.FilePath,
                snapshot.SessionId,
                snapshot.NodeId,
                cancellationToken)
            .ConfigureAwait(false);

        if (!HasSameDeltaDescriptor(snapshot, validated))
        {
            return Validation<PagedResult<SnapshotRecord>>(
                "Delta descriptor does not match its file.");
        }

        var totalPages = CalculateTotalPages(
            snapshot.RecordCount,
            pageSize);

        if ((totalPages == 0 && pageNumber != 1) ||
            (totalPages > 0 && pageNumber > totalPages))
        {
            return Validation<PagedResult<SnapshotRecord>>(
                "Page number exceeds the snapshot page count.");
        }

        return snapshot.StorageKind ==
            SnapshotStorageKind.DeltaKeep
            ? await ReadDeltaKeepPageAsync(
                    snapshot,
                    pageNumber,
                    pageSize,
                    cancellationToken)
                .ConfigureAwait(false)
            : await ReadDeltaRemovePageAsync(
                    snapshot,
                    pageNumber,
                    pageSize,
                    cancellationToken)
                .ConfigureAwait(false);
    }

    private static async Task<Result<PagedResult<SnapshotRecord>>>
        ReadDeltaKeepPageAsync(
            SnapshotDescriptor snapshot,
            long pageNumber,
            int pageSize,
            CancellationToken cancellationToken)
    {
        var startRecord = checked(
            (pageNumber - 1) * (long)pageSize);
        var itemCount = snapshot.RecordCount == 0
            ? 0
            : (int)Math.Min(
                pageSize,
                snapshot.RecordCount - startRecord);
        var items = new SnapshotRecord[itemCount];
        await using var stream = OpenRead(snapshot.FilePath);
        stream.Position = checked(
            DeltaHeaderSize +
            startRecord * snapshot.RecordSize);
        var buffer = new byte[snapshot.RecordSize];

        for (var index = 0; index < itemCount; index++)
        {
            await stream.ReadExactlyAsync(
                    buffer,
                    cancellationToken)
                .ConfigureAwait(false);
            items[index] = ParseRecord(
                buffer,
                snapshot.ValueSize);
        }

        return Result<PagedResult<SnapshotRecord>>.Success(
            new PagedResult<SnapshotRecord>(
                items,
                pageNumber,
                pageSize,
                snapshot.RecordCount));
    }

    private async Task<Result<PagedResult<SnapshotRecord>>>
        ReadDeltaRemovePageAsync(
            SnapshotDescriptor snapshot,
            long pageNumber,
            int pageSize,
            CancellationToken cancellationToken)
    {
        var header = await ReadValidatedDeltaHeaderAsync(
                snapshot.FilePath,
                cancellationToken)
            .ConfigureAwait(false);
        var removed = new HashSet<ulong>();
        var updates = new Dictionary<ulong, byte[]>();
        await using (var stream = OpenRead(snapshot.FilePath))
        {
            stream.Position = DeltaHeaderSize;
            var addressBuffer = new byte[sizeof(ulong)];

            for (long index = 0;
                 index < header.RemoveCount;
                 index++)
            {
                await stream.ReadExactlyAsync(
                        addressBuffer,
                        cancellationToken)
                    .ConfigureAwait(false);
                removed.Add(
                    BinaryPrimitives.ReadUInt64LittleEndian(
                        addressBuffer));
            }

            var recordBuffer = new byte[snapshot.RecordSize];

            for (long index = 0;
                 index < header.UpdateCount;
                 index++)
            {
                await stream.ReadExactlyAsync(
                        recordBuffer,
                        cancellationToken)
                    .ConfigureAwait(false);
                var record = ParseRecord(
                    recordBuffer,
                    snapshot.ValueSize);
                updates.Add(
                    record.Candidate.Address,
                    record.Value.ToArray());
            }
        }

        var parentResult = await OpenAsync(
                snapshot.SessionId,
                snapshot.ParentNodeId!.Value,
                cancellationToken)
            .ConfigureAwait(false);

        if (parentResult.IsFailure)
        {
            return Result<PagedResult<SnapshotRecord>>.Failure(
                parentResult.Error);
        }

        var startIndex = checked(
            (pageNumber - 1) * (long)pageSize);
        var endIndex = Math.Min(
            snapshot.RecordCount,
            checked(startIndex + pageSize));
        var result = new List<SnapshotRecord>(
            (int)(endIndex - startIndex));
        long logicalIndex = 0;

        await foreach (var record in EnumerateRecordsAsync(
            parentResult.Value,
            cancellationToken))
        {
            if (removed.Contains(record.Candidate.Address))
            {
                continue;
            }

            if (logicalIndex >= startIndex &&
                logicalIndex < endIndex)
            {
                result.Add(
                    updates.TryGetValue(
                        record.Candidate.Address,
                        out var value)
                        ? new SnapshotRecord(
                            record.Candidate,
                            value)
                        : record);
            }

            logicalIndex++;

            if (logicalIndex >= endIndex)
            {
                break;
            }
        }

        if (result.Count != endIndex - startIndex)
        {
            return Result<PagedResult<SnapshotRecord>>.Failure(
                new Error(
                    ErrorCode.Serialization,
                    "Delta reconstruction did not produce " +
                    "the declared candidate count."));
        }

        return Result<PagedResult<SnapshotRecord>>.Success(
            new PagedResult<SnapshotRecord>(
                result,
                pageNumber,
                pageSize,
                snapshot.RecordCount));
    }

    private async IAsyncEnumerable<SnapshotRecord>
        EnumerateRecordsAsync(
            SnapshotDescriptor snapshot,
            [EnumeratorCancellation]
            CancellationToken cancellationToken = default)
    {
        const int pageSize = 4096;
        var pageCount = CalculateTotalPages(
            snapshot.RecordCount,
            pageSize);

        for (long pageNumber = 1;
             pageNumber <= pageCount;
             pageNumber++)
        {
            var pageResult = await ReadPageAsync(
                    snapshot,
                    pageNumber,
                    pageSize,
                    cancellationToken)
                .ConfigureAwait(false);

            if (pageResult.IsFailure)
            {
                throw new InvalidDataException(
                    pageResult.Error.ToDisplayMessage(),
                    pageResult.Error.Exception);
            }

            foreach (var record in pageResult.Value.Items)
            {
                yield return record;
            }
        }
    }

    private async Task<SnapshotDescriptor> ValidateDeltaFileAsync(
        string path,
        Guid expectedSessionId,
        int expectedNodeId,
        CancellationToken cancellationToken)
    {
        var header = await ReadValidatedDeltaHeaderAsync(
                path,
                cancellationToken)
            .ConfigureAwait(false);

        if (header.SessionId != expectedSessionId ||
            header.NodeId != expectedNodeId)
        {
            throw new InvalidDataException(
                "Delta identity does not match its path.");
        }

        await using var stream = OpenRead(path);

        if (stream.Length !=
            checked(DeltaHeaderSize + header.PayloadLength))
        {
            throw new InvalidDataException(
                "Delta file length does not match its header.");
        }

        stream.Position = DeltaHeaderSize;
        using var checksum = IncrementalHash.CreateHash(
            HashAlgorithmName.SHA256);
        var buffer = new byte[IoBufferSize];
        long remaining = header.PayloadLength;

        while (remaining > 0)
        {
            var read = await stream.ReadAsync(
                    buffer.AsMemory(
                        0,
                        (int)Math.Min(buffer.Length, remaining)),
                    cancellationToken)
                .ConfigureAwait(false);

            if (read == 0)
            {
                throw new EndOfStreamException(
                    "Delta payload is incomplete.");
            }

            checksum.AppendData(buffer.AsSpan(0, read));
            remaining -= read;
        }

        if (!CryptographicOperations.FixedTimeEquals(
            checksum.GetHashAndReset(),
            header.Checksum))
        {
            throw new InvalidDataException(
                "Delta checksum validation failed.");
        }

        var referenceCount = await GetReferenceCountAsync(
                header.SessionId,
                header.NodeId,
                cancellationToken)
            .ConfigureAwait(false);
        return new SnapshotDescriptor(
            header.SessionId,
            header.NodeId,
            DeltaFormatVersion,
            header.ValueType,
            header.IncludesValues,
            header.ValueSize,
            header.RecordSize,
            header.RecordCount,
            header.PayloadLength,
            Convert.ToHexString(header.Checksum),
            header.CreatedAt,
            Path.GetFullPath(path),
            header.Kind,
            header.ParentNodeId,
            header.ChainDepth,
            header.AccumulatedDeltaBytes,
            referenceCount);
    }

    private static async Task<DeltaHeader>
        ReadValidatedDeltaHeaderAsync(
            string path,
            CancellationToken cancellationToken)
    {
        await using var stream = OpenRead(path);

        if (stream.Length < DeltaHeaderSize)
        {
            throw new InvalidDataException(
                "Delta header is incomplete.");
        }

        var buffer = new byte[DeltaHeaderSize];
        await stream.ReadExactlyAsync(
                buffer,
                cancellationToken)
            .ConfigureAwait(false);

        if (!buffer.AsSpan(0, 8).SequenceEqual(DeltaMagic) ||
            BinaryPrimitives.ReadInt32LittleEndian(
                buffer.AsSpan(8)) != DeltaFormatVersion ||
            BinaryPrimitives.ReadInt32LittleEndian(
                buffer.AsSpan(12)) != DeltaHeaderSize)
        {
            throw new InvalidDataException(
                "Delta format header is invalid.");
        }

        var kind = (SnapshotStorageKind)
            BinaryPrimitives.ReadInt32LittleEndian(
                buffer.AsSpan(16));
        var valueType = (ScanValueType)
            BinaryPrimitives.ReadInt32LittleEndian(
                buffer.AsSpan(20));
        var includesValues =
            BinaryPrimitives.ReadInt32LittleEndian(
                buffer.AsSpan(24)) != 0;
        var valueSize = BinaryPrimitives.ReadInt32LittleEndian(
            buffer.AsSpan(28));
        var recordSize = BinaryPrimitives.ReadInt32LittleEndian(
            buffer.AsSpan(32));
        var chainDepth = BinaryPrimitives.ReadInt32LittleEndian(
            buffer.AsSpan(36));
        var recordCount = BinaryPrimitives.ReadInt64LittleEndian(
            buffer.AsSpan(40));
        var payloadLength = BinaryPrimitives.ReadInt64LittleEndian(
            buffer.AsSpan(48));
        var sessionId = new Guid(buffer.AsSpan(56, 16));
        var nodeId = BinaryPrimitives.ReadInt32LittleEndian(
            buffer.AsSpan(72));
        var parentNodeId = BinaryPrimitives.ReadInt32LittleEndian(
            buffer.AsSpan(76));
        var accumulated = BinaryPrimitives.ReadInt64LittleEndian(
            buffer.AsSpan(80));
        var removeCount = BinaryPrimitives.ReadInt64LittleEndian(
            buffer.AsSpan(88));
        var updateCount = BinaryPrimitives.ReadInt64LittleEndian(
            buffer.AsSpan(96));
        var createdAt = DateTimeOffset.FromUnixTimeMilliseconds(
            BinaryPrimitives.ReadInt64LittleEndian(
                buffer.AsSpan(104)));
        var checksum = buffer
            .AsSpan(112, SHA256.HashSizeInBytes)
            .ToArray();
        var expectedValueSize = includesValues
            ? ScanValueTypeInfo.GetSize(valueType)
            : 0;
        var expectedPayload = kind switch
        {
            SnapshotStorageKind.DeltaKeep =>
                checked(recordCount * recordSize),
            SnapshotStorageKind.DeltaRemove =>
                checked(
                    removeCount * sizeof(ulong) +
                    updateCount * recordSize),
            _ => -1,
        };

        if (sessionId == Guid.Empty ||
            nodeId <= 0 ||
            parentNodeId <= 0 ||
            parentNodeId == nodeId ||
            chainDepth <= 0 ||
            chainDepth > MaximumDeltaChainDepth ||
            recordCount < 0 ||
            payloadLength < 0 ||
            accumulated < 0 ||
            removeCount < 0 ||
            updateCount < 0 ||
            valueSize != expectedValueSize ||
            recordSize != sizeof(ulong) + valueSize ||
            payloadLength != expectedPayload)
        {
            throw new InvalidDataException(
                "Delta header metadata is inconsistent.");
        }

        return new DeltaHeader(
            kind,
            sessionId,
            nodeId,
            parentNodeId,
            valueType,
            includesValues,
            valueSize,
            recordSize,
            recordCount,
            payloadLength,
            chainDepth,
            accumulated,
            removeCount,
            updateCount,
            createdAt,
            checksum);
    }

    private static async Task WriteDeltaFileAsync(
        string path,
        DeltaHeader header,
        IReadOnlyList<string> payloadPaths,
        CancellationToken cancellationToken)
    {
        await using var destination = OpenWrite(path);
        await destination.WriteAsync(
                CreateDeltaHeaderBuffer(header),
                cancellationToken)
            .ConfigureAwait(false);
        using var checksum = IncrementalHash.CreateHash(
            HashAlgorithmName.SHA256);
        var buffer = new byte[IoBufferSize];

        foreach (var payloadPath in payloadPaths)
        {
            await using var source = OpenRead(payloadPath);
            int read;

            while ((read = await source.ReadAsync(
                    buffer,
                    cancellationToken)
                .ConfigureAwait(false)) > 0)
            {
                checksum.AppendData(buffer.AsSpan(0, read));
                await destination.WriteAsync(
                        buffer.AsMemory(0, read),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        var completedHeader = header with
        {
            Checksum = checksum.GetHashAndReset(),
        };
        destination.Position = 0;
        await destination.WriteAsync(
                CreateDeltaHeaderBuffer(completedHeader),
                cancellationToken)
            .ConfigureAwait(false);
        await destination.FlushAsync(cancellationToken)
            .ConfigureAwait(false);
        destination.Flush(flushToDisk: true);
    }

    private static byte[] CreateDeltaHeaderBuffer(
        DeltaHeader header)
    {
        var buffer = new byte[DeltaHeaderSize];
        DeltaMagic.CopyTo(buffer, 0);
        BinaryPrimitives.WriteInt32LittleEndian(
            buffer.AsSpan(8),
            DeltaFormatVersion);
        BinaryPrimitives.WriteInt32LittleEndian(
            buffer.AsSpan(12),
            DeltaHeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(
            buffer.AsSpan(16),
            (int)header.Kind);
        BinaryPrimitives.WriteInt32LittleEndian(
            buffer.AsSpan(20),
            (int)header.ValueType);
        BinaryPrimitives.WriteInt32LittleEndian(
            buffer.AsSpan(24),
            header.IncludesValues ? 1 : 0);
        BinaryPrimitives.WriteInt32LittleEndian(
            buffer.AsSpan(28),
            header.ValueSize);
        BinaryPrimitives.WriteInt32LittleEndian(
            buffer.AsSpan(32),
            header.RecordSize);
        BinaryPrimitives.WriteInt32LittleEndian(
            buffer.AsSpan(36),
            header.ChainDepth);
        BinaryPrimitives.WriteInt64LittleEndian(
            buffer.AsSpan(40),
            header.RecordCount);
        BinaryPrimitives.WriteInt64LittleEndian(
            buffer.AsSpan(48),
            header.PayloadLength);
        header.SessionId.TryWriteBytes(buffer.AsSpan(56, 16));
        BinaryPrimitives.WriteInt32LittleEndian(
            buffer.AsSpan(72),
            header.NodeId);
        BinaryPrimitives.WriteInt32LittleEndian(
            buffer.AsSpan(76),
            header.ParentNodeId);
        BinaryPrimitives.WriteInt64LittleEndian(
            buffer.AsSpan(80),
            header.AccumulatedDeltaBytes);
        BinaryPrimitives.WriteInt64LittleEndian(
            buffer.AsSpan(88),
            header.RemoveCount);
        BinaryPrimitives.WriteInt64LittleEndian(
            buffer.AsSpan(96),
            header.UpdateCount);
        BinaryPrimitives.WriteInt64LittleEndian(
            buffer.AsSpan(104),
            header.CreatedAt.ToUnixTimeMilliseconds());
        header.Checksum.CopyTo(buffer, 112);
        return buffer;
    }

    private async Task<int> GetReferenceCountAsync(
        Guid sessionId,
        int nodeId,
        CancellationToken cancellationToken)
    {
        var count = 0;
        var sessionDirectory = GetSessionDirectory(sessionId);

        if (!Directory.Exists(sessionDirectory))
        {
            return 0;
        }

        foreach (var path in Directory.EnumerateFiles(
            sessionDirectory,
            "node_*.delta.bin",
            SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var header = await ReadValidatedDeltaHeaderAsync(
                        path,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (header.SessionId == sessionId &&
                    header.ParentNodeId == nodeId)
                {
                    count++;
                }
            }
            catch (Exception exception) when (
                exception is InvalidDataException or
                    EndOfStreamException or
                    CryptographicException)
            {
            }
        }

        return count;
    }

    private async Task RebuildReferenceIndexAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var sessionDirectory = GetSessionDirectory(sessionId);
        Directory.CreateDirectory(sessionDirectory);
        var nodeIds = EnumerateSnapshotPaths(sessionId)
            .Select(GetNodeIdFromFileName)
            .Distinct()
            .Order()
            .ToArray();
        var references = new SortedDictionary<int, int>();

        foreach (var nodeId in nodeIds)
        {
            references[nodeId] = await GetReferenceCountAsync(
                    sessionId,
                    nodeId,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var finalPath = Path.Combine(
            sessionDirectory,
            ReferenceIndexFileName);
        var temporaryPath =
            $"{finalPath}.tmp-{Guid.NewGuid():N}";
        var committed = false;

        try
        {
            await using (var stream = OpenWrite(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(
                        stream,
                        references,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            File.Move(
                temporaryPath,
                finalPath,
                overwrite: true);
            committed = true;
        }
        finally
        {
            if (!committed)
            {
                DeleteIfExists(temporaryPath);
            }
        }
    }

    private IEnumerable<string> EnumerateSnapshotPaths(
        Guid sessionId)
    {
        var sessionDirectory = GetSessionDirectory(sessionId);

        return Directory.Exists(sessionDirectory)
            ? Directory.EnumerateFiles(
                sessionDirectory,
                "node_*.bin",
                SearchOption.TopDirectoryOnly)
            : [];
    }

    private string? GetExistingSnapshotPath(
        Guid sessionId,
        int nodeId)
    {
        var candidates = new[]
        {
            GetSnapshotPath(sessionId, nodeId),
            GetDeltaSnapshotPath(
                sessionId,
                nodeId,
                SnapshotStorageKind.DeltaKeep),
            GetDeltaSnapshotPath(
                sessionId,
                nodeId,
                SnapshotStorageKind.DeltaRemove),
        }.Where(File.Exists).ToArray();

        return candidates.Length switch
        {
            0 => null,
            1 => candidates[0],
            _ => throw new InvalidDataException(
                "Multiple storage files exist for one snapshot node."),
        };
    }

    private string GetDeltaSnapshotPath(
        Guid sessionId,
        int nodeId,
        SnapshotStorageKind kind)
    {
        var suffix = kind switch
        {
            SnapshotStorageKind.DeltaKeep => "keep",
            SnapshotStorageKind.DeltaRemove => "remove",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        return Path.Combine(
            GetSessionDirectory(sessionId),
            $"node_{nodeId:D4}.{suffix}.delta.bin");
    }

    private static bool IsDeltaPath(string path)
    {
        return path.EndsWith(
            ".delta.bin",
            StringComparison.OrdinalIgnoreCase);
    }

    private static int GetNodeIdFromFileName(string path)
    {
        var name = Path.GetFileName(path);
        var firstDot = name.IndexOf('.');
        var idText = name.AsSpan(
            "node_".Length,
            firstDot - "node_".Length);
        return int.Parse(
            idText,
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static SnapshotDescriptor WithReferenceCount(
        SnapshotDescriptor descriptor,
        int referenceCount)
    {
        return new SnapshotDescriptor(
            descriptor.SessionId,
            descriptor.NodeId,
            descriptor.FormatVersion,
            descriptor.ValueType,
            descriptor.IncludesValues,
            descriptor.ValueSize,
            descriptor.RecordSize,
            descriptor.RecordCount,
            descriptor.PayloadLength,
            descriptor.Checksum,
            descriptor.CreatedAt,
            descriptor.FilePath,
            descriptor.StorageKind,
            descriptor.ParentNodeId,
            descriptor.ChainDepth,
            descriptor.AccumulatedDeltaBytes,
            referenceCount);
    }

    private static bool HasSameDeltaDescriptor(
        SnapshotDescriptor expected,
        SnapshotDescriptor actual)
    {
        return expected.SessionId == actual.SessionId &&
               expected.NodeId == actual.NodeId &&
               expected.ValueType == actual.ValueType &&
               expected.RecordCount == actual.RecordCount &&
               expected.Checksum.Equals(
                   actual.Checksum,
                   StringComparison.OrdinalIgnoreCase) &&
               expected.StorageKind == actual.StorageKind &&
               expected.ParentNodeId == actual.ParentNodeId &&
               expected.ChainDepth == actual.ChainDepth &&
               expected.AccumulatedDeltaBytes ==
                   actual.AccumulatedDeltaBytes &&
               Path.GetFullPath(expected.FilePath).Equals(
                   Path.GetFullPath(actual.FilePath),
                   StringComparison.OrdinalIgnoreCase);
    }

    private static async Task WriteAddressAsync(
        Stream stream,
        ulong address,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(
            buffer,
            address);
        await stream.WriteAsync(buffer, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task WriteRecordAsync(
        Stream stream,
        SnapshotRecord record,
        int valueSize,
        CancellationToken cancellationToken)
    {
        if (record.Value.Length != valueSize)
        {
            throw new InvalidDataException(
                "Delta record value size is invalid.");
        }

        var buffer = new byte[sizeof(ulong) + valueSize];
        BinaryPrimitives.WriteUInt64LittleEndian(
            buffer,
            record.Candidate.Address);
        record.Value.Span.CopyTo(buffer.AsSpan(sizeof(ulong)));
        await stream.WriteAsync(buffer, cancellationToken)
            .ConfigureAwait(false);
    }

    private static SnapshotRecord ParseRecord(
        ReadOnlySpan<byte> buffer,
        int valueSize)
    {
        return new SnapshotRecord(
            new CandidateAddress(
                BinaryPrimitives.ReadUInt64LittleEndian(buffer)),
            valueSize == 0
                ? ReadOnlyMemory<byte>.Empty
                : buffer.Slice(sizeof(ulong), valueSize)
                    .ToArray());
    }

    private static FileStream OpenRawWrite(string path)
    {
        return new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            IoBufferSize,
            FileOptions.Asynchronous |
            FileOptions.SequentialScan);
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private sealed record DeltaHeader(
        SnapshotStorageKind Kind,
        Guid SessionId,
        int NodeId,
        int ParentNodeId,
        ScanValueType ValueType,
        bool IncludesValues,
        int ValueSize,
        int RecordSize,
        long RecordCount,
        long PayloadLength,
        int ChainDepth,
        long AccumulatedDeltaBytes,
        long RemoveCount,
        long UpdateCount,
        DateTimeOffset CreatedAt,
        byte[] Checksum);
}
