using System;
using System.IO;
using System.Text;
using PowerGauge.Domain;

namespace PowerGauge.Diagnostics;

public sealed class ProbeLogWriter
{
    public string? Write(MousePowerSnapshot snapshot)
    {
        try
        {
            var logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppIdentity.LogDirectoryName);
            Directory.CreateDirectory(logDirectory);

            var logPath = Path.Combine(logDirectory, "probe.log");
            var builder = new StringBuilder();
            builder.AppendLine($"Timestamp: {snapshot.Timestamp:O}");
            builder.AppendLine($"DeviceDisplayName: {snapshot.DeviceDisplayName}");
            builder.AppendLine($"BatteryPercent: {(snapshot.BatteryPercent is int battery ? battery : "n/a")}");
            builder.AppendLine($"IsCharging: {(snapshot.IsCharging is bool isCharging ? isCharging : "n/a")}");
            builder.AppendLine($"IsSuccessful: {snapshot.IsSuccessful}");
            builder.AppendLine($"FailureKind: {snapshot.FailureKind}");
            builder.AppendLine($"ResultDetail: {snapshot.ResultDetail}");

            builder.AppendLine("Diagnostics:");
            builder.AppendLine(snapshot.Diagnostics);
            File.WriteAllText(logPath, builder.ToString());
            return logPath;
        }
        catch
        {
            return null;
        }
    }
}
