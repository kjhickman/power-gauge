using System;

namespace PowerGauge.Updates;

public sealed record AppVersion(string Normalized, Version Numeric) : IComparable<AppVersion>
{
    public string DisplayTag => $"v{Normalized}";

    public int CompareTo(AppVersion? other) => other is null ? 1 : Numeric.CompareTo(other.Numeric);

    public static bool TryParse(string? rawValue, out AppVersion? version)
    {
        version = null;
        var trimmed = rawValue?.Trim().TrimStart('v', 'V');
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return false;
        }

        var suffixIndex = trimmed.IndexOfAny(['-', '+']);
        var core = suffixIndex >= 0 ? trimmed[..suffixIndex] : trimmed;
        if (!Version.TryParse(core, out var parsed))
        {
            return false;
        }

        var canonical = new Version(
            parsed.Major,
            parsed.Minor,
            Math.Max(0, parsed.Build),
            Math.Max(0, parsed.Revision));

        version = new AppVersion(core, canonical);
        return true;
    }
}
