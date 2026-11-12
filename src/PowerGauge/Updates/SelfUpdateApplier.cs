using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace PowerGauge.Updates;

internal static class SelfUpdateApplier
{
    public static string GetStagingDirectory(string version)
    {
        var baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppIdentity.LogDirectoryName,
            "updates",
            version);

        if (Directory.Exists(baseDir))
        {
            Directory.Delete(baseDir, recursive: true);
        }

        Directory.CreateDirectory(baseDir);
        return baseDir;
    }

    public static void OpenBrowserUrl(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true,
        });
    }

    public static void LaunchInstallerAndScheduleRestart(
        InstallEnvironmentInfo environment,
        string downloadedAssetPath,
        string stagingDirectory)
    {
        var currentPid = Environment.ProcessId;

        switch (environment.Kind)
        {
            case InstallKind.WindowsInstaller:
                LaunchWindowsInstallerUpdate(environment, downloadedAssetPath, stagingDirectory, currentPid);
                break;

            case InstallKind.WindowsPortable:
                LaunchWindowsPortableUpdate(environment, downloadedAssetPath, stagingDirectory, currentPid);
                break;

            case InstallKind.MacOsAppBundle:
                LaunchMacOsBundleUpdate(environment, downloadedAssetPath, stagingDirectory, currentPid);
                break;

            default:
                throw new InvalidOperationException($"Self-update is not supported for install kind '{environment.Kind}'.");
        }
    }

    private static void LaunchWindowsInstallerUpdate(
        InstallEnvironmentInfo environment,
        string setupExePath,
        string stagingDirectory,
        int currentPid)
    {
        var executablePath = environment.ExecutablePath
            ?? Path.Combine(environment.AppDirectory, $"{AppIdentity.ExecutableName}.exe");
        var scriptPath = Path.Combine(stagingDirectory, "apply-update.ps1");

        var script = $$"""
            $ErrorActionPreference = 'SilentlyContinue'
            Wait-Process -Id {{currentPid}} -Timeout 30
            Start-Sleep -Milliseconds 300
            $runKeyPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
            $existingRun = Get-ItemProperty -LiteralPath $runKeyPath -Name '{{EscapePowerShellSingleQuoted(AppIdentity.ProductName)}}'
            $startupTaskArg = if ($null -ne $existingRun -and $null -ne $existingRun.'{{EscapePowerShellSingleQuoted(AppIdentity.ProductName)}}') { '/TASKS=startup' } else { '/TASKS=!startup' }
            $appDir = '{{EscapePowerShellSingleQuoted(environment.AppDirectory)}}'
            $dirArg = '/DIR="' + $appDir + '"'
            $proc = Start-Process -FilePath '{{EscapePowerShellSingleQuoted(setupExePath)}}' -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', $dirArg, $startupTaskArg) -Wait -PassThru
            if ($null -ne $proc -and $proc.ExitCode -eq 0 -and (Test-Path -LiteralPath '{{EscapePowerShellSingleQuoted(executablePath)}}')) {
                Start-Process -FilePath '{{EscapePowerShellSingleQuoted(executablePath)}}'
            }
            Remove-Item -LiteralPath '{{EscapePowerShellSingleQuoted(stagingDirectory)}}' -Recurse -Force
            """;

        File.WriteAllText(scriptPath, script, Encoding.UTF8);
        StartHiddenPowerShellScript(scriptPath);
    }

    private static void LaunchWindowsPortableUpdate(
        InstallEnvironmentInfo environment,
        string zipPath,
        string stagingDirectory,
        int currentPid)
    {
        var extractedDirectory = Path.Combine(stagingDirectory, "extracted");
        ZipFile.ExtractToDirectory(zipPath, extractedDirectory, overwriteFiles: true);

        var stagedExe = Path.Combine(extractedDirectory, $"{AppIdentity.ExecutableName}.exe");
        if (!File.Exists(stagedExe))
        {
            throw new FileNotFoundException("Extracted portable archive did not contain PowerGauge.exe.", stagedExe);
        }

        var targetExe = environment.ExecutablePath
            ?? Path.Combine(environment.AppDirectory, $"{AppIdentity.ExecutableName}.exe");
        var scriptPath = Path.Combine(stagingDirectory, "apply-update.ps1");

        var script = $$"""
            $ErrorActionPreference = 'SilentlyContinue'
            Wait-Process -Id {{currentPid}} -Timeout 30
            Start-Sleep -Milliseconds 300
            Copy-Item -Path (Join-Path '{{EscapePowerShellSingleQuoted(extractedDirectory)}}' '*') -Destination '{{EscapePowerShellSingleQuoted(environment.AppDirectory)}}' -Recurse -Force
            if (Test-Path -LiteralPath '{{EscapePowerShellSingleQuoted(targetExe)}}') {
                Start-Process -FilePath '{{EscapePowerShellSingleQuoted(targetExe)}}'
            }
            Remove-Item -LiteralPath '{{EscapePowerShellSingleQuoted(stagingDirectory)}}' -Recurse -Force
            """;

        File.WriteAllText(scriptPath, script, Encoding.UTF8);
        StartHiddenPowerShellScript(scriptPath);
    }

    private static void LaunchMacOsBundleUpdate(
        InstallEnvironmentInfo environment,
        string zipPath,
        string stagingDirectory,
        int currentPid)
    {
        var bundlePath = environment.MacOsAppBundlePath
            ?? throw new InvalidOperationException("macOS app bundle path is unavailable.");
        var extractedDirectory = Path.Combine(stagingDirectory, "extracted");
        var scriptPath = Path.Combine(stagingDirectory, "apply-update.sh");

        var script = $$"""
            #!/bin/sh
            PID={{currentPid}}
            COUNT=0
            while kill -0 "$PID" 2>/dev/null && [ "$COUNT" -lt 150 ]; do
                sleep 0.2
                COUNT=$((COUNT + 1))
            done
            EXTRACT_DIR='{{EscapeShSingleQuoted(extractedDirectory)}}'
            mkdir -p "$EXTRACT_DIR"
            if ditto -x -k '{{EscapeShSingleQuoted(zipPath)}}' "$EXTRACT_DIR"; then
                NEW_APP="$EXTRACT_DIR/{{AppIdentity.ProductName}}.app"
                if [ -d "$NEW_APP" ]; then
                    xattr -cr "$NEW_APP" 2>/dev/null || true
                    chmod +x "$NEW_APP/Contents/MacOS/{{AppIdentity.ExecutableName}}" 2>/dev/null || true
                    rm -rf '{{EscapeShSingleQuoted(bundlePath)}}'
                    mv "$NEW_APP" '{{EscapeShSingleQuoted(bundlePath)}}'
                    open '{{EscapeShSingleQuoted(bundlePath)}}'
                fi
            fi
            rm -rf '{{EscapeShSingleQuoted(stagingDirectory)}}'
            """;

        File.WriteAllText(scriptPath, script.Replace("\r\n", "\n", StringComparison.Ordinal), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var startInfo = new ProcessStartInfo
        {
            FileName = "/bin/sh",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetTempPath(),
        };
        startInfo.ArgumentList.Add(scriptPath);

        if (Process.Start(startInfo) is null)
        {
            throw new InvalidOperationException("Failed to start macOS update helper script.");
        }
    }

    private static void StartHiddenPowerShellScript(string scriptPath)
    {
        var systemPowerShell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        var powerShellExe = File.Exists(systemPowerShell) ? systemPowerShell : "powershell.exe";

        var startInfo = new ProcessStartInfo
        {
            FileName = powerShellExe,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetTempPath(),
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);

        if (Process.Start(startInfo) is null)
        {
            throw new InvalidOperationException("Failed to start Windows update helper script.");
        }
    }

    private static string EscapePowerShellSingleQuoted(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }

    private static string EscapeShSingleQuoted(string value)
    {
        return value.Replace("'", "'\\''", StringComparison.Ordinal);
    }
}
