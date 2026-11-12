using System;
using System.Globalization;

namespace PowerGauge.Updates;

public sealed class AppVersion : IComparable<AppVersion>, IEquatable<AppVersion>
{
    private readonly int[] _numbers;
    private readonly string[] _prereleaseIdentifiers;

    private AppVersion(string normalized, int[] numbers, string[] prereleaseIdentifiers)
    {
        Normalized = normalized;
        _numbers = numbers;
        _prereleaseIdentifiers = prereleaseIdentifiers;
    }

    public string Normalized { get; }

    public string DisplayTag => $"v{Normalized}";

    public bool IsPrerelease => _prereleaseIdentifiers.Length > 0;

    public static bool TryParse(string? rawValue, out AppVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        var trimmed = rawValue.Trim();
        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
        {
            trimmed = trimmed[1..];
        }

        var metadataIndex = trimmed.IndexOf('+');
        if (metadataIndex >= 0)
        {
            trimmed = trimmed[..metadataIndex];
        }

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return false;
        }

        string corePart;
        string[] prereleaseParts;
        var prereleaseIndex = trimmed.IndexOf('-');
        if (prereleaseIndex >= 0)
        {
            corePart = trimmed[..prereleaseIndex];
            var prereleaseText = trimmed[(prereleaseIndex + 1)..];
            if (string.IsNullOrWhiteSpace(prereleaseText))
            {
                return false;
            }

            prereleaseParts = prereleaseText.Split('.');
        }
        else
        {
            corePart = trimmed;
            prereleaseParts = [];
        }

        var rawSegments = corePart.Split('.');
        if (rawSegments.Length is < 1 or > 4)
        {
            return false;
        }

        var numbers = new int[4];
        for (var i = 0; i < rawSegments.Length; i++)
        {
            if (!int.TryParse(rawSegments[i], NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            {
                return false;
            }

            numbers[i] = parsed;
        }

        version = new AppVersion(trimmed, numbers, prereleaseParts);
        return true;
    }

    public int CompareTo(AppVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        for (var i = 0; i < _numbers.Length; i++)
        {
            var comparison = _numbers[i].CompareTo(other._numbers[i]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        if (_prereleaseIdentifiers.Length == 0 && other._prereleaseIdentifiers.Length == 0)
        {
            return 0;
        }

        if (_prereleaseIdentifiers.Length == 0)
        {
            return 1;
        }

        if (other._prereleaseIdentifiers.Length == 0)
        {
            return -1;
        }

        var count = Math.Min(_prereleaseIdentifiers.Length, other._prereleaseIdentifiers.Length);
        for (var i = 0; i < count; i++)
        {
            var left = _prereleaseIdentifiers[i];
            var right = other._prereleaseIdentifiers[i];
            var leftIsInt = int.TryParse(left, NumberStyles.None, CultureInfo.InvariantCulture, out var leftInt);
            var rightIsInt = int.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out var rightInt);

            int partComparison;
            if (leftIsInt && rightIsInt)
            {
                partComparison = leftInt.CompareTo(rightInt);
            }
            else if (leftIsInt != rightIsInt)
            {
                partComparison = leftIsInt ? -1 : 1;
            }
            else
            {
                partComparison = string.CompareOrdinal(left, right);
            }

            if (partComparison != 0)
            {
                return partComparison;
            }
        }

        return _prereleaseIdentifiers.Length.CompareTo(other._prereleaseIdentifiers.Length);
    }

    public bool Equals(AppVersion? other) => other is not null && CompareTo(other) == 0;

    public override bool Equals(object? obj) => obj is AppVersion other && Equals(other);

    public override int GetHashCode()
    {
        return HashCode.Combine(_numbers[0], _numbers[1], _numbers[2], _numbers[3], IsPrerelease);
    }

    public override string ToString() => Normalized;
}
