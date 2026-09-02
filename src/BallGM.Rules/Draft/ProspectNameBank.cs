using BallGM.Domain.Randomness;

namespace BallGM.Rules.Draft;

/// <summary>
/// The built-in fictional name pool <see cref="ProspectGenerator"/> draws from. Shipped content, not
/// a rule — the generator's algorithm is what Milestone 8 makes configurable; the specific fictional
/// names a build ships are exactly the kind of thing a data pack replaces once the mod platform
/// (Milestone 10) exists. Entirely invented, none drawn from a real player, coach, or public figure.
/// </summary>
internal static class ProspectNameBank
{
    private static readonly string[] FirstNames =
    [
        "Marcus", "Devon", "Julian", "Theo", "Kade", "Rafe", "Isaiah", "Malik", "Corey", "Jonas",
        "Ezra", "Sam", "Dario", "Elian", "Trent", "Owen", "Reggie", "Bryce", "Nash", "Omari",
        "Kian", "Silas", "Tobias", "Wesley", "Zane",
    ];

    private static readonly string[] LastNames =
    [
        "Ashworth", "Bellamy", "Carrow", "Doyle", "Emberton", "Farris", "Grier", "Holloway", "Ibarra", "Jencks",
        "Kestrel", "Lachlan", "Marrow", "Novak", "Osgood", "Pryce", "Quill", "Renfro", "Sabatini", "Tremaine",
        "Ustinov", "Vance", "Whitlock", "Yarrow", "Zabel",
    ];

    public static string NextName(IRandomSource random)
    {
        var first = FirstNames[random.NextInt32(0, FirstNames.Length)];
        var last = LastNames[random.NextInt32(0, LastNames.Length)];
        return $"{first} {last}";
    }
}
