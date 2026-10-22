using BallGM.Application.Leagues;
using BallGM.Application.Packs;

namespace BallGM.Mods.Checking;

/// <summary>The checker's one-word answer.</summary>
public enum PackCheckVerdict
{
    /// <summary>Loads, plays, and looks the way the packs say. Nothing to read unless you want to.</summary>
    Stable,

    /// <summary>Loads and plays; something will look or behave differently from what the packs say. Read the warnings.</summary>
    StableWithWarnings,

    /// <summary>Will not load or will not play. The game refuses this setup until the errors are fixed.</summary>
    Blocked,
}

/// <summary>One step of the load, in the order the game performs it.</summary>
public sealed record LoadStep(int Order, PackCheckStage Stage, string Description, string? PackId = null);

/// <summary>How much of the league a kind of cosmetic covers once every layer is applied.</summary>
public sealed record CosmeticCoverage(string Kind, int Covered, int Total)
{
    public int Missing => Total - Covered;
}

/// <summary>
/// Everything the checker found, in the order it found it, and what it will do. A report is the
/// whole conversation between the game and a player who installed packs: it is what the Mods screen
/// shows, what <c>ballgm-packs check</c> prints, and what a bug report pastes.
/// </summary>
public sealed record PackCheckReport(
    string SetupName,
    PackCheckVerdict Verdict,
    IReadOnlyList<PackFinding> Findings,
    IReadOnlyList<LoadStep> LoadPlan,
    IReadOnlyList<CosmeticCoverage> Coverage)
{
    /// <summary>What the loader needs to load exactly what was checked. Absent when the setup is blocked.</summary>
    public PackLoadPlan? Plan { get; init; }

    public IEnumerable<PackFinding> Errors => Findings.Where(finding => finding.Severity == PackFindingSeverity.Error);

    public IEnumerable<PackFinding> Warnings => Findings.Where(finding => finding.Severity == PackFindingSeverity.Warning);

    public static PackCheckVerdict VerdictFor(IEnumerable<PackFinding> findings)
    {
        var severities = findings.Select(finding => finding.Severity).ToList();
        return severities.Contains(PackFindingSeverity.Error) ? PackCheckVerdict.Blocked
            : severities.Contains(PackFindingSeverity.Warning) ? PackCheckVerdict.StableWithWarnings
            : PackCheckVerdict.Stable;
    }
}

/// <summary>
/// A checked setup, resolved: the exact ruleset text, the league source, and the winning cosmetic
/// for every key. The loader applies this and nothing else, so what loads is what was checked.
/// </summary>
public sealed record PackLoadPlan(
    string RulesetJson,
    LeagueSource League,
    IReadOnlyDictionary<string, string> TeamLogos,
    IReadOnlyDictionary<string, string> PlayerPortraits,
    IReadOnlyDictionary<string, TeamColours> TeamColours,
    string? LeagueLogo);
