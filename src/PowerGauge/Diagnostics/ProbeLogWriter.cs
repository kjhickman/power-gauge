using System;
using System.IO;
using System.Text;
using PowerGauge.Domain;

namespace PowerGauge.Diagnostics;

public sealed class ProbeLogWriter
{
    private readonly string? _customLogDirectory;
    private readonly bool _isEnabled;

    public ProbeLogWriter(string? logDirectory = null, bool isEnabled = true)
    {
        _customLogDirectory = logDirectory;
        _isEnabled = isEnabled;
    }

    public static ProbeLogWriter Disabled { get; } = new(isEnabled: false);

    public string? Write(PowerProbeReport report)
    {
        if (!_isEnabled)
        {
            return null;
        }

        try
        {
            var logDirectory = _customLogDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppIdentity.LogDirectoryName);
            Directory.CreateDirectory(logDirectory);

            var logPath = Path.Combine(logDirectory, "probe.log");
            var builder = new StringBuilder();
            builder.AppendLine($"Timestamp: {report.Timestamp:O}");
            builder.AppendLine($"DetectedDevices: {report.Devices.Count}");
            builder.AppendLine($"FailureKind: {report.FailureKind}");
            builder.AppendLine($"ResultDetail: {report.ResultDetail}");

            foreach (var device in report.Devices)
            {
                builder.AppendLine("---");
                builder.AppendLine($"DeviceKey: {device.DeviceKey}");
                builder.AppendLine($"DeviceKind: {device.DeviceKind}");
                builder.AppendLine($"DeviceDisplayName: {device.DeviceDisplayName}");
                builder.AppendLine($"BatteryPercent: {(device.BatteryPercent is int battery ? battery : "n/a")}");
                builder.AppendLine($"IsCharging: {(device.IsCharging is bool isCharging ? isCharging : "n/a")}");
                builder.AppendLine($"IsSuccessful: {device.IsSuccessful}");
                builder.AppendLine($"FailureKind: {device.FailureKind}");
                builder.AppendLine($"ResultDetail: {device.ResultDetail}");
            }

            builder.AppendLine("---");
            builder.AppendLine("Diagnostics:");
            builder.AppendLine(report.Diagnostics);
            File.WriteAllText(logPath, builder.ToString());
            return logPath;
        }
        catch
        {
            return null;
        }
    }
}
