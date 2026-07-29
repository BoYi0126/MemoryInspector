using System.Text;

namespace MemoryInspector.Wpf.ViewModels;

public sealed class HexViewerRowViewModel
{
    private readonly byte[] _data;

    public HexViewerRowViewModel(
        ulong address,
        ulong offset,
        ReadOnlySpan<byte> data,
        int requestedLength,
        ulong? matchAddress = null,
        int matchLength = 0)
    {
        if (requestedLength is <= 0 or > HexViewerViewModel.BytesPerRow)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedLength));
        }

        if (data.Length > requestedLength)
        {
            throw new ArgumentException(
                "Row data cannot exceed its requested length.",
                nameof(data));
        }

        Address = address;
        Offset = offset;
        RequestedLength = requestedLength;
        _data = data.ToArray();
        HexDisplay = BuildHexDisplay();
        AsciiDisplay = BuildAsciiDisplay();
        HasUnreadableBytes = _data.Length < RequestedLength;
        IsSearchMatch = matchAddress.HasValue &&
            matchLength > 0 &&
            RangesOverlap(
                Address,
                (ulong)RequestedLength,
                matchAddress.Value,
                (ulong)matchLength);
    }

    public ulong Address { get; }

    public string AddressDisplay => $"0x{Address:X16}";

    public ulong Offset { get; }

    public string OffsetDisplay => $"+0x{Offset:X8}";

    public int RequestedLength { get; }

    public string HexDisplay { get; }

    public string AsciiDisplay { get; }

    public bool HasUnreadableBytes { get; }

    public bool IsSearchMatch { get; }

    private string BuildHexDisplay()
    {
        var builder = new StringBuilder(
            HexViewerViewModel.BytesPerRow * 3);

        for (var index = 0;
             index < HexViewerViewModel.BytesPerRow;
             index++)
        {
            if (index > 0)
            {
                builder.Append(' ');
            }

            if (index < _data.Length)
            {
                builder.Append(_data[index].ToString("X2"));
            }
            else if (index < RequestedLength)
            {
                builder.Append("??");
            }
            else
            {
                builder.Append("  ");
            }
        }

        return builder.ToString();
    }

    private string BuildAsciiDisplay()
    {
        var builder = new StringBuilder(
            HexViewerViewModel.BytesPerRow);

        for (var index = 0; index < RequestedLength; index++)
        {
            if (index >= _data.Length)
            {
                builder.Append('·');
                continue;
            }

            var value = _data[index];
            builder.Append(value is >= 0x20 and <= 0x7E
                ? (char)value
                : '.');
        }

        return builder.ToString();
    }

    private static bool RangesOverlap(
        ulong leftStart,
        ulong leftLength,
        ulong rightStart,
        ulong rightLength)
    {
        var leftEnd = leftStart + leftLength;
        var rightEnd = rightStart + rightLength;
        return leftStart < rightEnd && rightStart < leftEnd;
    }
}
