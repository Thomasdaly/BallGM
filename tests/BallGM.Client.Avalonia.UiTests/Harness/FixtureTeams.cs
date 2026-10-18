using System.Text.Json.Nodes;

namespace BallGM.Client.Avalonia.UiTests.Harness;

/// <summary>The 30 fixture teams' abbreviations and colours, read from the committed league pack.</summary>
internal static class FixtureTeams
{
    public sealed record Team(string Key, string Name, string Primary, string Secondary);

    public static IReadOnlyList<Team> All { get; } = Load();

    private static IReadOnlyList<Team> Load()
    {
        var pack = JsonNode.Parse(File.ReadAllText(RepoPaths.UiLeaguePack))!;
        return pack["teams"]!.AsArray()
            .Select(team => new Team(
                team!["key"]!.GetValue<string>(),
                team["name"]!.GetValue<string>(),
                team["colours"]!["primary"]!.GetValue<string>(),
                team["colours"]!["secondary"]!.GetValue<string>()))
            .ToList();
    }
}
