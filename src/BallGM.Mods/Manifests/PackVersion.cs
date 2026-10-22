using System.Globalization;

namespace BallGM.Mods.Manifests;

/// <summary>
/// A pack's version: <c>major.minor.patch</c>, three non-negative integers and nothing else. No
/// pre-release tags or build metadata — a pack author who needs "beta" has a version number to say it
/// with, and a comparison that has to rank "1.2.0-rc.1" against "1.2.0" is a comparison two pack
/// authors will read differently.
/// <para>
/// The convention, stated rather than enforced: a <b>major</b> bump means keys or content moved in a
/// way that breaks packs built on this one (a roster pack that renamed its player keys); a
/// <b>minor</b> bump adds content; a <b>patch</b> fixes it. Ranges in <see cref="VersionRange"/> are
/// written to follow that convention.
/// </para>
/// </summary>
public readonly record struct PackVersion(int Major, int Minor, int Patch) : IComparable<PackVersion>
{
    public static bool TryParse(string? text, out PackVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Trim().Split('.');
        if (parts.Length != 3)
        {
            return false;
        }

        var numbers = new int[3];
        for (var index = 0; index < 3; index++)
        {
            if (parts[index].Length == 0 ||
                !parts[index].All(char.IsAsciiDigit) ||
                !int.TryParse(parts[index], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[index]))
            {
                return false;
            }
        }

        version = new PackVersion(numbers[0], numbers[1], numbers[2]);
        return true;
    }

    public int CompareTo(PackVersion other) =>
        Major != other.Major ? Major.CompareTo(other.Major)
        : Minor != other.Minor ? Minor.CompareTo(other.Minor)
        : Patch.CompareTo(other.Patch);

    public static bool operator <(PackVersion left, PackVersion right) => left.CompareTo(right) < 0;

    public static bool operator >(PackVersion left, PackVersion right) => left.CompareTo(right) > 0;

    public static bool operator <=(PackVersion left, PackVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >=(PackVersion left, PackVersion right) => left.CompareTo(right) >= 0;

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}");
}
