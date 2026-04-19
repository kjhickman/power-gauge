using System.Reflection;

namespace PowerGauge;

public static class AppIdentity
{
    public const string ProductName = "PowerGauge";
    public const string ExecutableName = "PowerGauge";
    public const string LogDirectoryName = "PowerGauge";

    public static string DisplayVersion
    {
        get
        {
            var informationalVersion = typeof(AppIdentity).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;

            if (!string.IsNullOrWhiteSpace(informationalVersion))
            {
                var metadataSeparator = informationalVersion.IndexOf('+');
                return metadataSeparator >= 0
                    ? informationalVersion[..metadataSeparator]
                    : informationalVersion;
            }

            return typeof(AppIdentity).Assembly.GetName().Version?.ToString() ?? "dev";
        }
    }
}
