using System.Text.RegularExpressions;

namespace BallGM.Application.Leagues;

/// <summary>
/// Makes a rule explanation readable by putting names where the rules layer wrote identifiers.
/// Rules explain themselves against identifiers because that is all they hold — a rule never needs a
/// team's name — so "Team '01M3F…' has no backup at PointGuard" is correct but unreadable. This is
/// the presentation boundary that knows both, and it rewrites only identifiers it can resolve,
/// leaving everything else (including the machine-readable rule code, which travels separately)
/// untouched.
/// </summary>
public sealed partial class ExplanationNames
{
    private readonly IReadOnlyDictionary<string, string> _names;

    private ExplanationNames(IReadOnlyDictionary<string, string> names) => _names = names;

    public static ExplanationNames From(LeagueSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var franchise in snapshot.Franchises)
        {
            names[franchise.Id.Value] = franchise.Name;
        }

        foreach (var team in snapshot.Teams)
        {
            names[team.Id.Value] = team.Name;
        }

        foreach (var player in snapshot.Players)
        {
            names[player.Id.Value] = player.FullName;
        }

        return new ExplanationNames(names);
    }

    /// <summary>
    /// This set of names plus one more, for an entity the league snapshot does not hold — a draft
    /// prospect in a generated preview class has a name but is not a player yet.
    /// </summary>
    public ExplanationNames With(string identifier, string name)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        ArgumentNullException.ThrowIfNull(name);

        return new ExplanationNames(new Dictionary<string, string>(_names, StringComparer.Ordinal) { [identifier] = name });
    }

    public string Humanize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        // "Team '<id>'" / "player '<id>'" / "'<id>'" → the name alone; then any bare identifier. A
        // possessive follows the name: "Boston Celtics'" rather than "Boston Celtics's".
        var quoted = QuotedIdentifier().Replace(text, match =>
        {
            if (!_names.TryGetValue(match.Groups["id"].Value, out var name))
            {
                return match.Value;
            }

            return match.Groups["possessive"].Success
                ? name.EndsWith('s') ? name + "'" : name + "'s"
                : name;
        });
        return BareIdentifier().Replace(quoted, match =>
            _names.TryGetValue(match.Value, out var name) ? name : match.Value);
    }

    // SortableId values are 26-character Crockford base-32 ULIDs.
    [GeneratedRegex(@"(?:\b(?:[Tt]eam|[Pp]layer|[Ff]ranchise) )?'(?<id>[0-9A-HJKMNP-TV-Z]{26})'(?<possessive>'s\b)?")]
    private static partial Regex QuotedIdentifier();

    [GeneratedRegex(@"\b[0-9A-HJKMNP-TV-Z]{26}\b")]
    private static partial Regex BareIdentifier();
}
