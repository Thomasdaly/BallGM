using BallGM.Application.Leagues;
using BallGM.Application.Players;
using BallGM.Domain.Seasons;

namespace BallGM.Application.Seasons;

/// <summary>
/// One player's line on the league's stats table: who, where, every counting stat and his average
/// match rating. Rates are derived from the totals by <see cref="StatFormulas"/>.
/// </summary>
public sealed record LeaguePlayerLine(
    string PlayerId,
    string FullName,
    string? TeamName,
    string Position,
    CountingStats Stats,
    RatingAverage? Rating);

/// <summary>
/// Every player who has played this phase, the league's qualifiers, and the season's length so far —
/// the most games any team has played, which is what the qualifiers measure against. Not a player's
/// current team's count: a player traded mid-season played for teams whose games add up to the
/// season, not to his new team's total.
/// </summary>
public sealed record LeagueStatsSummary(IReadOnlyList<LeaguePlayerLine> Players, LeaderQualifiersConfiguration Qualifiers, int SeasonGames)
{
    /// <summary>
    /// Whether a player may be ranked on a stat (by its column code) on a basis — the one place the
    /// leaderboards and the percentiles take it from. The shooting percentages need the games share
    /// and their makes whatever the basis; a match rating the games share; a counting stat the games
    /// share per game, the minutes as well per 36, and nothing at all as a total.
    /// </summary>
    public bool Qualifies(string stat, LeaguePlayerLine line, StatBasis basis)
    {
        ArgumentNullException.ThrowIfNull(line);
        var games = StatFormulas.QualifiesOnGames(line.Stats.Games, SeasonGames, Qualifiers.MinimumGamesShareBasisPoints);
        return stat switch
        {
            "FG%" or "eFG%" or "TS%" => games && StatFormulas.QualifiesOnMakes(line.Stats.FieldGoalsMade, SeasonGames, Qualifiers.MinimumFieldGoalsMadePerTeamGame),
            "3P%" => games && StatFormulas.QualifiesOnMakes(line.Stats.ThreePointsMade, SeasonGames, Qualifiers.MinimumThreesMadePerTeamGame),
            "FT%" => games && StatFormulas.QualifiesOnMakes(line.Stats.FreeThrowsMade, SeasonGames, Qualifiers.MinimumFreeThrowsMadePerTeamGame),
            "RTG" => games,
            _ => basis switch
            {
                StatBasis.Totals => true,
                StatBasis.Per36 => games && line.Stats.Minutes >= line.Stats.Games * Qualifiers.MinimumMinutesPerGame,
                _ => games,
            },
        };
    }

    /// <summary>On the games share alone: who the table lists by default and who has percentiles.</summary>
    public bool QualifiesOnGames(LeaguePlayerLine line) => Qualifies("RTG", line, StatBasis.PerGame);
}

/// <summary>Where a player stands among the qualified on one stat, 0–100, higher always better.</summary>
public sealed record StatPercentile(string Stat, int Percentile, bool LowerIsBetter);
