using System.Globalization;

namespace Civil3D.HybridMcp.Protocol;

public readonly record struct ProtocolVersion(int Major, int Minor, int Patch) : IComparable<ProtocolVersion>
{
    public static ProtocolVersion Current { get; } = new(1, 0, 0);

    public static ProtocolVersion Parse(string value)
    {
        if (!TryParse(value, out var parsed))
        {
            throw new FormatException($"Invalid semantic version: {value}");
        }

        return parsed;
    }

    public static bool TryParse(string? value, out ProtocolVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split('.', StringSplitOptions.TrimEntries);
        if (parts.Length != 3)
        {
            return false;
        }

        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor) ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var patch))
        {
            return false;
        }

        if (major < 0 || minor < 0 || patch < 0)
        {
            return false;
        }

        version = new ProtocolVersion(major, minor, patch);
        return true;
    }

    public int CompareTo(ProtocolVersion other)
    {
        var major = Major.CompareTo(other.Major);
        if (major != 0)
        {
            return major;
        }

        var minor = Minor.CompareTo(other.Minor);
        if (minor != 0)
        {
            return minor;
        }

        return Patch.CompareTo(other.Patch);
    }

    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}

