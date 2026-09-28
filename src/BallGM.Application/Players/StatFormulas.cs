namespace BallGM.Application.Players;

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
        var attempts = 2 * (fga + (0.44 * fta));
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
        minutes == 0 || availableRebounds == 0 ? null : rebounds * (teamMinutes / 5.0) / (minutes * (double)availableRebounds);

    /// <summary>A counting stat per 36 minutes — the standard rate for comparing players across roles.</summary>
    public static double? Per36(int total, int minutes) => minutes == 0 ? null : total * 36.0 / minutes;

    public static double? PerGame(int total, int games) => games == 0 ? null : (double)total / games;
}
