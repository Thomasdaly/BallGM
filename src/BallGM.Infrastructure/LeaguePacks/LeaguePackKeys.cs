using System.Globalization;
using System.Text;

namespace BallGM.Infrastructure.LeaguePacks;

/// <summary>
/// The stable, human-readable keys a league pack's players are addressed by from outside the pack —
/// a face pack names a file after one. A team's key is always stated; a player's is optional, and
/// absent it is derived from what the pack already says about him: his name and birth date, as
/// <c>aldo-brenner-2001-03-14</c>.
/// <para>
/// Deliberately not the loaded <c>PlayerId</c>: identifiers are minted fresh on every load, so a
/// cosmetic pack keyed on them would map faces to nobody the next time the game started. A derived
/// key stays put for as long as the pack does not rename the player; one the pack states stays put
/// for ever, which is why a pack meant to carry cosmetics should state them.
/// </para>
/// </summary>
public static class LeaguePackKeys
{
    /// <summary>The longest key a pack may state: long enough for any name, short enough to be a file name everywhere.</summary>
    public const int MaximumLength = 64;

    /// <summary>
    /// Whether <paramref name="key"/> is a key a pack may state: letters, digits, <c>.</c>,
    /// <c>_</c> and <c>-</c>, starting with a letter or digit. File-name safe on every platform the
    /// game ships on, and never a path.
    /// </summary>
    public static bool IsValid(string? key) =>
        !string.IsNullOrEmpty(key) &&
        key.Length <= MaximumLength &&
        char.IsAsciiLetterOrDigit(key[0]) &&
        key.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');

    /// <summary>The key a player who states none is known by: his name in lower-case ASCII, hyphenated, then his birth date.</summary>
    public static string Derive(string name, DateOnly birthDate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var slug = new StringBuilder(name.Length);
        foreach (var character in name.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsAsciiLetterOrDigit(character))
            {
                slug.Append(char.ToLowerInvariant(character));
            }
            else if (slug.Length > 0 && slug[^1] != '-')
            {
                slug.Append('-');
            }
        }

        var stem = slug.ToString().Trim('-');
        if (stem.Length == 0)
        {
            stem = "player";
        }

        var date = birthDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var room = MaximumLength - date.Length - 1;
        return $"{(stem.Length > room ? stem[..room].TrimEnd('-') : stem)}-{date}";
    }
}
