using BallGM.Domain.AI;
using BallGM.Domain.Common;
using BallGM.Domain.Leagues;
using BallGM.Domain.Players;
using BallGM.Domain.Seasons;
using BallGM.Domain.Teams;
using BallGM.Rules.Configuration;

namespace BallGM.Rules.AI;

/// <summary>
/// Reads a team's current record (or, before there is one, its roster strength) alongside its age
/// profile and classifies it as rebuilding, retooling, or contending. Deliberately two axes only —
/// "are we winning now" and "is our core young" — because a third axis (cap flexibility, asset
/// stockpile) has nothing yet to consume the distinction it would add, and a classifier tuned against
/// factors nothing reads is exactly the premature abstraction <c>CLAUDE.md</c> warns against.
/// <para>
/// Pure and total: given the same roster, standing, and rules, the same team classifies the same way
/// every time. No randomness, no mutation, no persisted state — this is a read model, the same
/// division of labour <c>TradeValidator</c>/<c>SigningValidator</c> keep between assessing and acting.
/// </para>
/// </summary>
public static class OrganisationalDirectionClassifier
{
    private const string WinPercentCode = "ai_direction.win_percent";
    private const string NoStandingsCode = "ai_direction.no_standings_yet";
    private const string YoungCoreCode = "ai_direction.young_core";
    private const string VeteranCoreCode = "ai_direction.veteran_core";

    /// <summary>The win percentage, expressed as a whole-number threshold, at or above which a team reads as competitive now.</summary>
    private const int ContendingWinPercent = 55;

    /// <summary>The average roster Overall, when no standings exist yet, that reads as competitive now.</summary>
    private const int StrongRosterOverall = 65;

    /// <summary>The share of the roster below the peak age, as a whole-number percentage, that reads as a young core.</summary>
    private const int YoungCoreMajorityPercent = 50;

    /// <summary>Fallback peak-age start when this league configures no development curve at all.</summary>
    private const int DefaultPeakAgeStart = 24;

    public static OrganisationalDirectionAssessment Classify(
        TeamId teamId,
        Season currentSeason,
        IReadOnlyList<Player> roster,
        StandingsRow? standing,
        DevelopmentRules developmentRules)
    {
        ArgumentNullException.ThrowIfNull(teamId);
        ArgumentNullException.ThrowIfNull(currentSeason);
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(developmentRules);

        var referenceDate = new DateOnly(currentSeason.Year, 10, 1);
        var peakAgeStart = developmentRules.IsConfigured ? developmentRules.PeakAgeStart : DefaultPeakAgeStart;

        var findings = new List<RuleFinding>();
        var isCompetitiveNow = ClassifyCompetitiveness(teamId, roster, standing, findings);
        var hasYoungCore = ClassifyAgeProfile(teamId, roster, referenceDate, peakAgeStart, findings);

        var direction = isCompetitiveNow
            ? CompetitiveDirection.Contending
            : hasYoungCore
                ? CompetitiveDirection.Retooling
                : CompetitiveDirection.Rebuilding;

        return new OrganisationalDirectionAssessment(teamId, direction, findings);
    }

    private static bool ClassifyCompetitiveness(
        TeamId teamId,
        IReadOnlyList<Player> roster,
        StandingsRow? standing,
        List<RuleFinding> findings)
    {
        if (standing is not null && standing.GamesPlayed > 0)
        {
            var record = standing.Overall;
            var isCompetitiveNow = record.Wins * 100 >= ContendingWinPercent * record.Games;
            findings.Add(new RuleFinding(
                WinPercentCode,
                $"Team '{teamId.Value}' is {record.Wins}-{record.Losses} this season, {(isCompetitiveNow ? "at or above" : "below")} the {ContendingWinPercent}% mark this reading treats as competitive.",
                teamId));
            return isCompetitiveNow;
        }

        var averageOverall = roster.Count == 0 ? 0 : (int)roster.Average(player => player.Rating.Overall);
        var isCompetitiveFromRoster = averageOverall >= StrongRosterOverall;
        findings.Add(new RuleFinding(
            NoStandingsCode,
            $"Team '{teamId.Value}' has no standings yet this season, so this reading falls back to average roster Overall ({averageOverall}) against a {StrongRosterOverall} threshold.",
            teamId));
        return isCompetitiveFromRoster;
    }

    private static bool ClassifyAgeProfile(
        TeamId teamId,
        IReadOnlyList<Player> roster,
        DateOnly referenceDate,
        int peakAgeStart,
        List<RuleFinding> findings)
    {
        var youngCoreShare = roster.Count == 0
            ? 0
            : roster.Count(player => player.AgeOn(referenceDate) < peakAgeStart) * 100 / roster.Count;
        var hasYoungCore = youngCoreShare >= YoungCoreMajorityPercent;

        findings.Add(new RuleFinding(
            hasYoungCore ? YoungCoreCode : VeteranCoreCode,
            hasYoungCore
                ? $"Team '{teamId.Value}' has {youngCoreShare}% of its roster below the peak age of {peakAgeStart}, a young core."
                : $"Team '{teamId.Value}' has only {youngCoreShare}% of its roster below the peak age of {peakAgeStart}, a veteran-leaning core.",
            teamId));

        return hasYoungCore;
    }
}
