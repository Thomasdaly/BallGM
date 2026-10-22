namespace BallGM.Mods.Manifests;

/// <summary>
/// Which versions of another pack a pack works with. Four spellings, and no others:
/// <list type="bullet">
/// <item><c>*</c> (or absent) — any version.</item>
/// <item><c>1.4.0</c> — exactly that version.</item>
/// <item><c>^1.4.0</c> — that version or any later one with the same major: what a pack built on
/// another one's keys almost always means.</item>
/// <item><c>&gt;=1.4.0 &lt;2.0.0</c> — comparators (<c>&gt;=</c>, <c>&gt;</c>, <c>&lt;=</c>, <c>&lt;</c>,
/// <c>=</c>) separated by spaces, all of which must hold.</item>
/// </list>
/// Deliberately smaller than npm's grammar: no <c>||</c>, no <c>~</c>, no wildcards inside a
/// version. Every extra spelling is one more way for a pack author and a player to disagree about
/// what a range admits, and the checker's job is to remove that disagreement, not to host it.
/// </summary>
public sealed record VersionRange
{
    private readonly IReadOnlyList<(string Operator, PackVersion Version)> _comparators;

    private VersionRange(string text, IReadOnlyList<(string Operator, PackVersion Version)> comparators)
    {
        Text = text;
        _comparators = comparators;
    }

    public static VersionRange Any { get; } = new("*", []);

    /// <summary>The range as written.</summary>
    public string Text { get; }

    public static bool TryParse(string? text, out VersionRange range)
    {
        range = Any;
        if (string.IsNullOrWhiteSpace(text) || text.Trim() == "*")
        {
            return true;
        }

        var trimmed = text.Trim();
        if (trimmed.StartsWith('^'))
        {
            if (!PackVersion.TryParse(trimmed[1..], out var floor))
            {
                return false;
            }

            range = new VersionRange(trimmed, [(">=", floor), ("<", new PackVersion(floor.Major + 1, 0, 0))]);
            return true;
        }

        var comparators = new List<(string, PackVersion)>();
        foreach (var token in trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var op = token.StartsWith(">=", StringComparison.Ordinal) ? ">="
                : token.StartsWith("<=", StringComparison.Ordinal) ? "<="
                : token.StartsWith('>') ? ">"
                : token.StartsWith('<') ? "<"
                : token.StartsWith('=') ? "="
                : string.Empty;

            if (!PackVersion.TryParse(token[op.Length..], out var version))
            {
                return false;
            }

            comparators.Add((op.Length == 0 ? "=" : op, version));
        }

        range = new VersionRange(trimmed, comparators);
        return true;
    }

    public bool Admits(PackVersion version) =>
        _comparators.All(comparator => comparator.Operator switch
        {
            ">=" => version >= comparator.Version,
            ">" => version > comparator.Version,
            "<=" => version <= comparator.Version,
            "<" => version < comparator.Version,
            _ => version == comparator.Version,
        });

    public override string ToString() => Text;
}
