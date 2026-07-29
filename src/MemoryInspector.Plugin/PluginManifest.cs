using System.Text.RegularExpressions;
using MemoryInspector.Common;

namespace MemoryInspector.Plugin;

public sealed partial record PluginManifest
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } =
        CurrentSchemaVersion;

    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public string ApiVersion { get; init; } = string.Empty;

    public string? MinimumHostVersion { get; init; }

    public string? MaximumHostVersion { get; init; }

    public string EntryAssembly { get; init; } = string.Empty;

    public string EntryType { get; init; } = string.Empty;

    public IReadOnlyList<PluginKind> Capabilities { get; init; } =
        [];

    public string? Description { get; init; }

    public string? Author { get; init; }

    public bool EnabledByDefault { get; init; } = true;

    public Result Validate(
        Version? hostVersion = null,
        Version? apiVersion = null)
    {
        hostVersion ??= PluginApiVersion.HostVersion;
        apiVersion ??= PluginApiVersion.Current;

        if (SchemaVersion != CurrentSchemaVersion)
        {
            return Failure(
                ErrorCode.Serialization,
                $"Unsupported plugin manifest schema " +
                $"{SchemaVersion}.");
        }

        if (!PluginIdRegex().IsMatch(Id))
        {
            return Failure(
                ErrorCode.Validation,
                "Plugin ID must contain 3–64 letters, digits, " +
                "periods, underscores, or hyphens and start with " +
                "a letter.");
        }

        if (string.IsNullOrWhiteSpace(Name) ||
            Name.Length > 100 ||
            string.IsNullOrWhiteSpace(EntryAssembly) ||
            Path.GetFileName(EntryAssembly) != EntryAssembly ||
            !EntryAssembly.EndsWith(
                ".dll",
                StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(EntryType))
        {
            return Failure(
                ErrorCode.Validation,
                "Plugin name, entry assembly, or entry type is " +
                "invalid.");
        }

        if (!TryParseVersion(Version, out _) ||
            !TryParseVersion(ApiVersion, out var requiredApi))
        {
            return Failure(
                ErrorCode.Validation,
                "Plugin and API versions must use numeric version " +
                "syntax.");
        }

        if (requiredApi.Major != apiVersion.Major ||
            requiredApi > apiVersion)
        {
            return Failure(
                ErrorCode.InvalidState,
                $"Plugin API {ApiVersion} is incompatible with host " +
                $"API {apiVersion}.");
        }

        if (!TryOptionalVersion(
                MinimumHostVersion,
                out var minimumHost) ||
            !TryOptionalVersion(
                MaximumHostVersion,
                out var maximumHost) ||
            (minimumHost is not null &&
             maximumHost is not null &&
             minimumHost > maximumHost))
        {
            return Failure(
                ErrorCode.Validation,
                "Plugin host-version range is invalid.");
        }

        if ((minimumHost is not null &&
             hostVersion < minimumHost) ||
            (maximumHost is not null &&
             hostVersion > maximumHost))
        {
            return Failure(
                ErrorCode.InvalidState,
                $"Plugin requires host version " +
                $"{MinimumHostVersion ?? "*"}–" +
                $"{MaximumHostVersion ?? "*"}; current host is " +
                $"{hostVersion}.");
        }

        if (Capabilities is null ||
            Capabilities.Count == 0 ||
            Capabilities.Any(kind => !Enum.IsDefined(kind)) ||
            Capabilities.Distinct().Count() != Capabilities.Count)
        {
            return Failure(
                ErrorCode.Validation,
                "Plugin capabilities must contain distinct supported " +
                "values.");
        }

        return Result.Success();
    }

    private static bool TryParseVersion(
        string? value,
        out Version version)
    {
        return System.Version.TryParse(value, out version!) &&
               version.Major >= 0;
    }

    private static bool TryOptionalVersion(
        string? value,
        out Version? version)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            version = null;
            return true;
        }

        if (TryParseVersion(value, out var parsed))
        {
            version = parsed;
            return true;
        }

        version = null;
        return false;
    }

    private static Result Failure(
        ErrorCode code,
        string message)
    {
        return Result.Failure(new Error(code, message));
    }

    [GeneratedRegex(
        "^[A-Za-z][A-Za-z0-9._-]{2,63}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex PluginIdRegex();
}
