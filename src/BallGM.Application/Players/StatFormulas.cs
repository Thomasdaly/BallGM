using BallGM.Domain.Seasons;

namespace BallGM.Application.Players;

/// <summary>How a counting stat is shown: as a total, per game played, or per 36 minutes on the floor.</summary>
public enum StatBasis
{
    Totals = 1,
    PerGame = 2,
    Per36 = 3,
}

/// <summary>
/// Basketball's derived statistics, as the league's own stats glossary defines them (NBA.com/stats
/// glossary), computed from counted totals. Pure functions of their inputs; each returns <c>null</c>
/// rather than a misleading zero when its denominator is empty (a player with no shots has no
/// shooting percentage, not 0%).
/// <para>
/// The rate stats that need "while on the floor" team totals — assist, rebound percentages — use the
/// standard minutes-share estimate (team totals scaled by the player's share of team minutes),
/// because the simulation records box scores, not who shared the floor with whom.
/// </para>
/// </summary>
public static class StatFormulas
{
    public static double? Percent(int made, int attempted) => attempted == 0 ? null : (double)made / attempted;

    /// <summary>eFG% = (FGM + 0.5 × 3PM) / FGA — a made three is worth one and a half twos.</summary>
    public static double? EffectiveFieldGoal(int fgm, int threesMade, int fga) =>
        fga == 0 ? null : (fgm + (0.5 * threesMade)) / fga;

    /// <summary>TS% = PTS / (2 × (FGA + 0.44 × FTA)) — scoring efficiency counting threes and free throws.</summary>
    public static double? TrueShooting(int points, int fga, int fta)
    {
        var attempts = 2 * (fga + (FreeThrowTripWeight * fta));
        return attempts == 0 ? null : points / attempts;
    }

    /// <summary>3PAr = 3PA / FGA — the share of shots taken from three.</summary>
    public static double? ThreePointRate(int threesAttempted, int fga) => fga == 0 ? null : (double)threesAttempted / fga;

    /// <summary>FTr = FTA / FGA — how often a player gets to the line per shot.</summary>
    public static double? FreeThrowRate(int fta, int fga) => fga == 0 ? null : (double)fta / fga;

    /// <summary>Share of a player's points from twos, threes, and free throws.</summary>
    public static (double TwoPoint, double ThreePoint, double FreeThrow)? PointsDistribution(int fgm, int threesMade, int ftm, int points)
    {
        if (points == 0)
        {
            return null;
        }

        return (2.0 * (fgm - threesMade) / points, 3.0 * threesMade / points, (double)ftm / points);
    }

    /// <summary>
    /// AST% = AST / ((MIN / (TmMIN / 5)) × TmFGM − FGM): the share of team-mates' field goals this
    /// player assisted while on the floor, estimated from his share of team minutes.
    /// </summary>
    public static double? AssistPercent(int assists, int minutes, int teamMinutes, int teamFgm, int fgm)
    {
        if (minutes == 0 || teamMinutes == 0)
        {
            return null;
        }

        var teammateFieldGoals = ((double)minutes / (teamMinutes / 5.0) * teamFgm) - fgm;
        return teammateFieldGoals <= 0 ? null : assists / teammateFieldGoals;
    }

    /// <summary>
    /// Rebound percentage = REB × (TmMIN / 5) / (MIN × available rebounds): the share of the rebounds
    /// available while on the floor that this player took. Used for offensive (available: team OREB +
    /// opponent DREB), defensive (team DREB + opponent OREB), and total rebounds.
    /// </summary>
    public static double? ReboundPercent(int rebounds, int minutes, int teamMinutes, int availableRebounds) =>
        OnFloorShare(rebounds, minutes, teamMinutes, availableRebounds);

    /// <summary>A counting stat per 36 minutes — the standard rate for comparing players across roles.</summary>
    public static double? Per36(int total, int minutes) => minutes == 0 ? null : total * 36.0 / minutes;

    public static double? PerGame(int total, int games) => games == 0 ? null : (double)total / games;

    /// <summary>One counting stat of <paramref name="stats"/> on the requested basis; null when there is nothing to divide by.</summary>
    public static double? Scaled(int total, CountingStats stats, StatBasis basis)
    {
        ArgumentNullException.ThrowIfNull(stats);

        return basis switch
        {
            StatBasis.Totals => total,
            StatBasis.PerGame => PerGame(total, stats.Games),
            StatBasis.Per36 => Per36(total, stats.Minutes),
            _ => throw new ArgumentOutOfRangeException(nameof(basis), basis, "Unknown stat basis."),
        };
    }

    public static double? FieldGoalPercent(CountingStats stats) => Percent(stats.FieldGoalsMade, stats.FieldGoalsAttempted);

    public static double? ThreePointPercent(CountingStats stats) => Percent(stats.ThreePointsMade, stats.ThreePointsAttempted);

    public static double? FreeThrowPercent(CountingStats stats) => Percent(stats.FreeThrowsMade, stats.FreeThrowsAttempted);

    public static double? TrueShooting(CountingStats stats) => TrueShooting(stats.Points, stats.FieldGoalsAttempted, stats.FreeThrowsAttempted);

    public static double? EffectiveFieldGoal(CountingStats stats) => EffectiveFieldGoal(stats.FieldGoalsMade, stats.ThreePointsMade, stats.FieldGoalsAttempted);

    /// <summary>
    /// The share of a possession one free-throw attempt stands for in a play or possession count: two-
    /// and three-shot trips, and-ones and technicals mean an attempt ends about 0.44 of a possession.
    /// </summary>
    private const double FreeThrowTripWeight = 0.44;

    /// <summary>Plays a player or side used: FGA + 0.44 × FTA + TOV — every way a possession can end in their hands.</summary>
    public static double Plays(CountingStats stats)
    {
        ArgumentNullException.ThrowIfNull(stats);
        return stats.FieldGoalsAttempted + (FreeThrowTripWeight * stats.FreeThrowsAttempted) + stats.Turnovers;
    }

    /// <summary>Possessions a side used, estimated from its box score: plays less offensive rebounds (FGA + 0.44 × FTA − OREB + TOV).</summary>
    public static double Possessions(CountingStats side) => Plays(side) - side.OffensiveRebounds;

    /// <summary>Points per 100 possessions — offensive rating for a side's own scoring, defensive rating for what it allowed.</summary>
    public static double? Rating(CountingStats side)
    {
        var possessions = Possessions(side);
        return possessions <= 0 ? null : 100.0 * side.Points / possessions;
    }

    /// <summary>Net rating: points scored less points allowed, each per 100 possessions.</summary>
    public static double? NetRating(CountingStats side, CountingStats opponent) => Rating(side) - Rating(opponent);

    /// <summary>TOV% = TOV / (FGA + 0.44 × FTA + TOV): turnovers per play used.</summary>
    public static double? TurnoverPercent(CountingStats stats)
    {
        var plays = Plays(stats);
        return plays <= 0 ? null : stats.Turnovers / plays;
    }

    /// <summary>AST/TO: assists per turnover.</summary>
    public static double? AssistToTurnover(CountingStats stats) => Percent(stats.Assists, stats.Turnovers);

    /// <summary>
    /// The share of a pool a player took while on the floor, estimated from share of team minutes:
    /// value × (TmMIN / 5) / (MIN × pool). Rebound, usage and steal percentages are all this shape.
    /// </summary>
    public static double? OnFloorShare(double value, int minutes, int teamMinutes, double pool) =>
        minutes == 0 || pool <= 0 ? null : value * (teamMinutes / 5.0) / (minutes * pool);

    /// <summary>USG%: the share of the team's plays a player used while on the floor.</summary>
    public static double? UsagePercent(CountingStats player, CountingStats team)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(team);
        return OnFloorShare(Plays(player), player.Minutes, team.Minutes, Plays(team));
    }

    /// <summary>STL%: the share of opponent possessions a player ended with a steal while on the floor.</summary>
    public static double? StealPercent(CountingStats player, CountingStats team, CountingStats opponent)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(team);
        return OnFloorShare(player.Steals, player.Minutes, team.Minutes, Possessions(opponent));
    }

    /// <summary>
    /// The four factors that decide a game, for one side against its opponent: shooting (eFG%),
    /// ball security (TOV%), offensive rebounding (OREB / (OREB + opponent DREB)) and getting to the
    /// line (FTM / FGA).
    /// </summary>
    public static FourFactors FourFactorsFor(CountingStats side, CountingStats opponent)
    {
        ArgumentNullException.ThrowIfNull(side);
        ArgumentNullException.ThrowIfNull(opponent);

        return new FourFactors(
            EffectiveFieldGoal(side),
            TurnoverPercent(side),
            Percent(side.OffensiveRebounds, side.OffensiveRebounds + opponent.DefensiveRebounds),
            Percent(side.FreeThrowsMade, side.FieldGoalsAttempted));
    }
}

/// <summary>
/// Dean Oliver's four factors for one side; each null when its denominator is empty. Higher is
/// better for every factor except <see cref="TurnoverPercent"/>, where lower is.
/// </summary>
public sealed record FourFactors(double? EffectiveFieldGoal, double? TurnoverPercent, double? OffensiveReboundPercent, double? FreeThrowsPerFieldGoal);
