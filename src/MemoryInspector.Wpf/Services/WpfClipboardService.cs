using System.Runtime.InteropServices;
using System.Windows;
using MemoryInspector.Common;

namespace MemoryInspector.Wpf.Services;

public sealed class WpfClipboardService : IClipboardService
{
    public Result SetText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Result.Failure(
                new Error(
                    ErrorCode.Validation,
                    "Clipboard text cannot be empty."));
        }

        try
        {
            Clipboard.SetText(text);
            return Result.Success();
        }
        catch (Exception exception) when (
            exception is ExternalException or
            ThreadStateException)
        {
            return Result.Failure(
                new Error(
                    ErrorCode.Unexpected,
                    "The address could not be copied to the clipboard.",
                    exception));
        }
    }
}
