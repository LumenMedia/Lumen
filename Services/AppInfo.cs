using System.Reflection;
using System.Text.RegularExpressions;

namespace Lumen.Services;

public static class AppInfo
{
    private const string FallbackVersion = "1.0.0-rc.1";

    public static string InformationalVersion { get; } = ReadInformationalVersion();
    public static string DisplayVersion { get; } = FormatDisplayVersion(InformationalVersion);

    private static string ReadInformationalVersion()
    {
        var value = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (string.IsNullOrWhiteSpace(value))
            return FallbackVersion;

        var metadataIndex = value.IndexOf('+');
        return metadataIndex >= 0 ? value[..metadataIndex] : value;
    }

    private static string FormatDisplayVersion(string version)
    {
        var match = Regex.Match(
            version,
            @"^(?<version>\d+\.\d+\.\d+)-rc[.-]?(?<rc>\d+)$",
            RegexOptions.IgnoreCase);

        return match.Success
            ? $"{match.Groups["version"].Value} RC{match.Groups["rc"].Value}"
            : version;
    }
}
