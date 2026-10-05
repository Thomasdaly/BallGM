using System.Globalization;
using System.Text;

namespace BallGM.Client.Avalonia.ViewModels;

/// <summary>Turns identifiers and counts into words a GM reads. Presentation only.</summary>
internal static class DisplayText
{
    /// <summary>"RegularSeason" → "Regular season", "HeadToHeadRecord" → "Head to head record".</summary>
    public static string Words(string identifier)
    {
        if (string.IsNullOrEmpty(identifier))
        {
            return identifier;
        }

        var builder = new StringBuilder(identifier.Length + 8);
        for (var index = 0; index < identifier.Length; index++)
        {
            var character = identifier[index];
            if (index > 0 && char.IsUpper(character) && !char.IsUpper(identifier[index - 1]))
            {
                builder.Append(' ');
                builder.Append(char.ToLowerInvariant(character));
            }
            else
            {
                builder.Append(index == 0 ? char.ToUpperInvariant(character) : character);
            }
        }

        return builder.ToString();
    }

    /// <summary>"1 day", "3 days".</summary>
    public static string Count(int count, string singular, string? plural = null) =>
        string.Create(CultureInfo.InvariantCulture, $"{count} {(count == 1 ? singular : plural ?? singular + "s")}");
}
