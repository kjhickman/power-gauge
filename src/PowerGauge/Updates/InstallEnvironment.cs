using System;
using System.IO;
using System.Linq;

namespace PowerGauge.Updates;

internal enum InstallKind
{
    Development,
    WindowsInstaller,
    WindowsPortable,
    MacOsAppBundle,
    Unsupported,
}

internal sealed record InstallEnvironmentInfo(
    InstallKind Kind,
    string AppDirectory,
    string? ExecutablePath,
    string? MacOsAppBundlePath = null);

internal static class InstallEnvironment
{
    public static InstallEnvironmentInfo Detect()
    {
        var baseDirectory = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        var processPath = Environment.ProcessPath;
        var managedAssemblyPath = Path.Combine(baseDirectory, $"{AppIdentity.ExecutableName}.dll");

        if (File.Exists(managedAssemblyPath))
        {
            return new InstallEnvironmentInfo(InstallKind.Development, baseDirectory, processPath);
        }

        if (OperatingSystem.IsWindows())
        {
            var executablePath = !string.IsNullOrWhiteSpace(processPath)
                ? processPath
                : Path.Combine(baseDirectory, $"{AppIdentity.ExecutableName}.exe");
            var uninstallerPath = Path.Combine(baseDirectory, "unins000.exe");
            var kind = File.Exists(uninstallerPath)
                ? InstallKind.WindowsInstaller
                : InstallKind.WindowsPortable;

            return new InstallEnvironmentInfo(kind, baseDirectory, executablePath);
        }

        if (OperatingSystem.IsMacOS())
        {
            var executablePath = !string.IsNullOrWhiteSpace(processPath)
                ? processPath
                : Path.Combine(baseDirectory, AppIdentity.ExecutableName);

            if (TryGetMacOsAppBundlePath(executablePath, out var bundlePath))
            {
                return new InstallEnvironmentInfo(InstallKind.MacOsAppBundle, baseDirectory, executablePath, bundlePath);
            }
        }

        return new InstallEnvironmentInfo(InstallKind.Unsupported, baseDirectory, processPath);
    }

    public static GitHubReleaseAssetDto? FindMatchingAsset(GitHubReleaseDto release, InstallKind installKind)
    {
        if (release.Assets is null || release.Assets.Count == 0)
        {
            return null;
        }

        return installKind switch
        {
            InstallKind.WindowsInstaller => release.Assets.FirstOrDefault(asset =>
                !string.IsNullOrWhiteSpace(asset.Name)
                && !string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl)
                && asset.Name.StartsWith("PowerGauge-windows-x64-setup-", StringComparison.OrdinalIgnoreCase)
                && asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)),

            InstallKind.WindowsPortable => release.Assets.FirstOrDefault(asset =>
                !string.IsNullOrWhiteSpace(asset.Name)
                && !string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl)
                && asset.Name.StartsWith("PowerGauge-windows-x64-", StringComparison.OrdinalIgnoreCase)
                && !asset.Name.Contains("-setup-", StringComparison.OrdinalIgnoreCase)
                && asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)),

            InstallKind.MacOsAppBundle => release.Assets.FirstOrDefault(asset =>
                !string.IsNullOrWhiteSpace(asset.Name)
                && !string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl)
                && asset.Name.StartsWith("PowerGauge-macos-arm64-", StringComparison.OrdinalIgnoreCase)
                && asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)),

            _ => null,
        };
    }

    private static bool TryGetMacOsAppBundlePath(string executablePath, out string? appBundlePath)
    {
        appBundlePath = null;
        var macOsDir = Path.GetDirectoryName(executablePath);
        if (macOsDir is null || !string.Equals(Path.GetFileName(macOsDir), "MacOS", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var contentsDir = Path.GetDirectoryName(macOsDir);
        if (contentsDir is null || !string.Equals(Path.GetFileName(contentsDir), "Contents", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var bundleDir = Path.GetDirectoryName(contentsDir);
        if (bundleDir is null || !bundleDir.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        appBundlePath = bundleDir;
        return true;
    }
}
