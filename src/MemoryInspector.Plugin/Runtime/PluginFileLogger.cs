using System.Globalization;
using System.Text;
using MemoryInspector.Common;

namespace MemoryInspector.Plugin.Runtime;

internal sealed class PluginFileLogger(
    string rootDirectory,
    string pluginId,
    TimeProvider timeProvider) : IPluginLogger
{
    private readonly object _sync = new();
    private readonly string _directory = Path.Combine(
        Path.GetFullPath(rootDirectory),
        Sanitize(pluginId));
    private readonly string _pluginId = pluginId;
    private readonly TimeProvider _timeProvider = timeProvider;

    public Result Log(
        PluginLogLevel level,
        string message,
        Exception? exception = null)
    {
        if (!Enum.IsDefined(level) ||
            string.IsNullOrWhiteSpace(message))
        {
            return Result.Failure(
                new Error(
                    ErrorCode.Validation,
                    "Plugin log level and message are required."));
        }

        try
        {
            lock (_sync)
            {
                Directory.CreateDirectory(_directory);
                var now = _timeProvider.GetLocalNow();
                var path = Path.Combine(
                    _directory,
                    $"{now:yyyy-MM-dd}.log");
                var builder = new StringBuilder()
                    .Append(now.ToString(
                        "O",
                        CultureInfo.InvariantCulture))
                    .Append(" [")
                    .Append(level)
                    .Append("] [")
                    .Append(_pluginId)
                    .Append("] ")
                    .AppendLine(message.Trim());

                if (exception is not null)
                {
                    builder.AppendLine(exception.ToString());
                }

                File.AppendAllText(
                    path,
                    builder.ToString(),
                    new UTF8Encoding(
                        encoderShouldEmitUTF8Identifier: false));
            }

            return Result.Success();
        }
        catch (Exception writeException) when (
            writeException is IOException or
            UnauthorizedAccessException or
            NotSupportedException)
        {
            return Result.Failure(
                new Error(
                    ErrorCode.Io,
                    "Plugin log could not be written.",
                    writeException));
        }
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(
            value.Select(character =>
                    invalid.Contains(character)
                        ? '_'
                        : character)
                .ToArray());
    }
}
