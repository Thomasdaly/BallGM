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

    /// <summary>A total per game; a stated metric's total may be fractional.</summary>
    public static double? PerGame(double total, int games) => games == 0 ? null : total / games;

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

    /// <summary>The fewest attempts from a zone for its percentage to be shown: fewer is noise, not a shooter.</summary>
    public const int MinimumZoneAttempts = 5;

    /// <summary>FG% from one zone; null with no attempts there.</summary>
    public static double? ZonePercent(ShotZones zones, ShotZone zone)
    {
        ArgumentNullException.ThrowIfNull(zones);
        return Percent(zones.Made(zone), zones.Attempts(zone));
    }

    /// <summary>
    /// A shooter's FG% from a zone less the league's from the same zone, in percentage points; null
    /// below <see cref="MinimumZoneAttempts"/> attempts, or when the league has none there.
    /// </summary>
    public static double? ZoneDifference(ShotZones player, ShotZones league, ShotZone zone)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(league);
        return player.Attempts(zone) < MinimumZoneAttempts || ZonePercent(league, zone) is not { } leagueRate
            ? null
            : 100 * (ZonePercent(player, zone)!.Value - leagueRate);
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
        ArgumentNullException.ThrowIfNull(opponent);
        return OnFloorShare(player.Steals, player.Minutes, team.Minutes, Possessions(opponent));
    }

    /// <summary>
    /// BLK%: blocks per opponent two-point attempt while on the floor — the league convention, which
    /// counts the rare blocked three in the numerator against two-point attempts only.
    /// </summary>
    public static double? BlockPercent(CountingStats player, CountingStats team, CountingStats opponent)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(team);
        ArgumentNullException.ThrowIfNull(opponent);
        return OnFloorShare(player.Blocks, player.Minutes, team.Minutes, opponent.TwoPointsAttempted);
    }

    // The match rating (see MatchRating): integer arithmetic throughout, every constant named.

    /// <summary>Where every rating starts: an average game is a 6.5.</summary>
    public const int RatingAnchor = 650;

    /// <summary>The floor and ceiling of the scale, in hundredths: 1.0 to 10.0.</summary>
    public const int MinimumRating = 100;

    /// <inheritdoc cref="MinimumRating"/>
    public const int MaximumRating = 1000;

    /// <summary>
    /// Par production a minute, in thousandths of a tenth of a Game Score point: set a little under the
    /// league's mean as this engine plays (3,729, measured over 3,000 games), so a player is rated on
    /// what he added over an average player in the same minutes and the league's mean rating sits just
    /// above the anchor. With <see cref="RatingPerProductionTenth"/> it gives mean 6.55, SD 0.78 and a
    /// 99th percentile of 8.67 — the shape a GM expects from a 1–10 rating.
    /// </summary>
    public const int ParProductionPerMinute = 3_580;

    /// <summary>Rating hundredths per tenth of a Game Score point over par, in thousandths (1,000 = one hundredth a tenth).</summary>
    public const int RatingPerProductionTenth = 1_700;

    /// <summary>On-court impact: rating hundredths per point of plus-minus, capped either way.</summary>
    public const int ImpactPerPoint = 3;

    /// <inheritdoc cref="ImpactPerPoint"/>
    public const int MaximumImpact = 60;

    /// <summary>Minutes of shrinkage toward the anchor: a short stint counts for less, in proportion MIN / (MIN + this).</summary>
    public const int ShrinkMinutes = 8;

    /// <summary>Tenths of a Game Score point a foul-out costs over and above the fouls themselves.</summary>
    public const int FoulOutPenalty = 20;

    /// <summary>
    /// One game's match rating, or null for a player who did not play: Hollinger's Game Score by
    /// family (in tenths), less par for his minutes, plus a bounded share of his plus-minus, shrunk
    /// toward 6.5 for a short stint and mapped onto the 1–10 scale. Every family is a term of the
    /// breakdown, the par for the minutes is a term of its own, and the terms sum exactly to the
    /// rating's distance from 6.5 — the rounding remainder (and any clamp) goes to the largest term.
    /// </summary>
    public static MatchRating? MatchRatingFor(CountingStats game)
    {
        ArgumentNullException.ThrowIfNull(game);
        if (game.Minutes <= 0)
        {
            return null;
        }

        Span<long> numerators = stackalloc long[RatingFamilies.Length];
        var denominator = Numerators(game, numerators);
        var rating = Rating(numerators, denominator);

        // Terms truncated toward zero, then reconciled so they add up to the rating.
        var terms = new List<RatingTerm>(RatingFamilies.Length);
        for (var index = 0; index < RatingFamilies.Length; index++)
        {
            var family = index == ParTerm ? $"Par for {game.Minutes} min" : RatingFamilies[index];
            terms.Add(new RatingTerm(family, (int)(numerators[index] / denominator)));
        }

        return new MatchRating(rating, ReconcileToLargest(terms, rating - RatingAnchor));
    }

    /// <summary>One game's rating in hundredths without its breakdown — for averages over a season, built per game.</summary>
    public static int? MatchRatingHundredths(CountingStats game)
    {
        ArgumentNullException.ThrowIfNull(game);
        if (game.Minutes <= 0)
        {
            return null;
        }

        Span<long> numerators = stackalloc long[RatingFamilies.Length];
        return Rating(numerators, Numerators(game, numerators));
    }

    /// <summary>The mean of the match ratings of the games played, and how many there were; null when none were.</summary>
    public static RatingAverage? AverageRating(IEnumerable<CountingStats> games)
    {
        ArgumentNullException.ThrowIfNull(games);

        long sum = 0;
        var count = 0;
        foreach (var game in games)
        {
            if (MatchRatingHundredths(game) is { } hundredths)
            {
                sum += hundredths;
                count++;
            }
        }

        return count == 0 ? null : MeanRating(sum, count);
    }

    /// <summary>The mean of <paramref name="games"/> ratings summing to <paramref name="sum"/> hundredths, rounded half up.</summary>
    public static RatingAverage MeanRating(long sum, int games) => new((int)((sum + (games / 2)) / games), games);

    /// <summary>A rating as shown, to one decimal: hundredths rounded half up to tenths.</summary>
    public static int ShownTenths(int hundredths) => (hundredths + 5) / 10;

    /// <summary>The band of a shown rating, in tenths: outstanding from 8.0, good from 7.0, average from 6.0.</summary>
    public static RatingBand RatingBandOf(int tenths) => tenths switch
    {
        >= 80 => RatingBand.Outstanding,
        >= 70 => RatingBand.Good,
        >= 60 => RatingBand.Average,
        _ => RatingBand.Poor,
    };

    /// <summary>
    /// The terms with whatever they fall short of (or over) <paramref name="target"/> given to the
    /// largest in size — the one rule every rating breakdown uses so its rows add up to its figure.
    /// </summary>
    internal static IReadOnlyList<RatingTerm> ReconcileToLargest(IReadOnlyList<RatingTerm> terms, int target)
    {
        var reconciled = terms.ToList();
        var largest = reconciled.IndexOf(reconciled.MaxBy(term => Math.Abs(term.Hundredths))!);
        reconciled[largest] = reconciled[largest] with { Hundredths = reconciled[largest].Hundredths + (target - reconciled.Sum(term => term.Hundredths)) };
        return reconciled;
    }

    private static readonly string[] RatingFamilies = ["Scoring", "Rebounding", "Playmaking", "Defence", "Discipline", "Par", "On-court impact"];

    private const int ParTerm = 5;

    /// <summary>
    /// Every term's numerator over one shared denominator, so nothing is truncated before the end:
    /// tenths × scale (thousandths) × MIN, with par a further thousandth finer, over
    /// 1,000,000 × (MIN + shrink). Returns the denominator.
    /// </summary>
    private static long Numerators(CountingStats game, Span<long> numerators)
    {
        long minutes = game.Minutes;
        long Term(long tenths) => tenths * RatingPerProductionTenth * minutes * 1_000;

        numerators[0] = Term((10L * game.Points) + (4L * game.FieldGoalsMade) - (7L * game.FieldGoalsAttempted) - (4L * (game.FreeThrowsAttempted - game.FreeThrowsMade)));
        numerators[1] = Term((7L * game.OffensiveRebounds) + (3L * game.DefensiveRebounds));
        numerators[2] = Term((7L * game.Assists) - (10L * game.Turnovers));
        numerators[3] = Term((10L * game.Steals) + (7L * game.Blocks));
        numerators[4] = Term((-4L * game.PersonalFouls) - (FoulOutPenalty * (long)game.FoulOuts));
        numerators[ParTerm] = -(long)ParProductionPerMinute * minutes * RatingPerProductionTenth * minutes;
        numerators[6] = Math.Clamp((long)game.PlusMinus * ImpactPerPoint, -MaximumImpact, MaximumImpact) * minutes * 1_000_000;

        return 1_000_000L * (minutes + ShrinkMinutes);
    }

    /// <summary>The terms' sum rounded half away from zero, added to the anchor and clamped to the scale.</summary>
    private static int Rating(ReadOnlySpan<long> numerators, long denominator)
    {
        long sum = 0;
        foreach (var numerator in numerators)
        {
            sum += numerator;
        }

        var total = (int)(((2 * sum) + (Math.Sign(sum) * denominator)) / (2 * denominator));
        return Math.Clamp(RatingAnchor + total, MinimumRating, MaximumRating);
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

/// <summary>The mean match rating over a run of games, in hundredths, and how many games were rated (played).</summary>
public sealed record RatingAverage(int Hundredths, int Games)
{
    /// <summary>The figure as shown, in tenths.</summary>
    public int Tenths => StatFormulas.ShownTenths(Hundredths);

    public RatingBand Band => StatFormulas.RatingBandOf(Tenths);
}

/// <summary>A rating's band: outstanding from 8.0, good from 7.0, average from 6.0, poor below.</summary>
public enum RatingBand
{
    Poor,
    Average,
    Good,
    Outstanding,
}

/// <summary>One named contribution to a <see cref="MatchRating"/>, in hundredths of a rating point.</summary>
public sealed record RatingTerm(string Family, int Hundredths);

/// <summary>
/// A match rating out of ten, in hundredths (785 is 7.85), with the terms it was built from. The terms
/// always sum exactly to <see cref="Hundredths"/> less <see cref="StatFormulas.RatingAnchor"/>; the
/// same terms in tenths, <see cref="TermsInTenths"/>, sum exactly to the shown figure, <see cref="Tenths"/>,
/// less the anchor — so a breakdown printed to one decimal adds up to the rating printed beside it.
/// </summary>
public sealed record MatchRating(int Hundredths, IReadOnlyList<RatingTerm> Terms)
{
    public int Tenths => StatFormulas.ShownTenths(Hundredths);

    public RatingBand Band => StatFormulas.RatingBandOf(Tenths);

    public IReadOnlyList<RatingTerm> TermsInTenths => StatFormulas.ReconcileToLargest(
        Terms.Select(term => term with { Hundredths = (int)Math.Round(term.Hundredths / 10.0, MidpointRounding.AwayFromZero) }).ToList(),
        Tenths - (StatFormulas.RatingAnchor / 10));
}

/// <summary>
/// Dean Oliver's four factors for one side; each null when its denominator is empty. Higher is
/// better for every factor except <see cref="TurnoverPercent"/>, where lower is.
/// </summary>
public sealed record FourFactors(double? EffectiveFieldGoal, double? TurnoverPercent, double? OffensiveReboundPercent, double? FreeThrowsPerFieldGoal);
