using System.Globalization;
using BallGM.Application.Players;

namespace BallGM.Client.Avalonia.ViewModels;

/// <summary>One labelled number on the stats tab, with the league's definition as its tooltip.</summary>
public sealed record StatCell(string Label, string Value, string Definition);

/// <summary>A titled block of stat cells ("Shooting & efficiency", "Hustle", ...).</summary>
public sealed record StatGroup(string Title, string Subtitle, IReadOnlyList<StatCell> Cells);

/// <summary>
/// Builds the stats tab's groups: the simulated season from counted box-score totals through
/// <see cref="StatFormulas"/>, and a data pack's stated real season from its metric codes. Every
/// label carries the definition the NBA's stats glossary gives it.
/// </summary>
internal static class PlayerStatsSections
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static IReadOnlyList<StatGroup> SimulatedSeason(SeasonDetailLine season)
    {
        var totals = season.Totals;
        var g = totals.Games;
        string PerGame(int total) => Fixed(StatFormulas.Scaled(total, totals, StatBasis.PerGame));
        string Per36(int total) => Fixed(StatFormulas.Scaled(total, totals, StatBasis.Per36));
        string MadeAttempted(int made, int attempted) => $"{PerGame(made)}-{PerGame(attempted)}";
        var distribution = StatFormulas.PointsDistribution(totals.FieldGoalsMade, totals.ThreePointsMade, totals.FreeThrowsMade, totals.Points);

        return
        [
            new("Traditional", $"Per game · {g} games, {totals.Starts} starts",
            [
                Cell("MIN", PerGame(totals.Minutes), "Minutes played per game."),
                Cell("PTS", PerGame(totals.Points), "Points per game."),
                Cell("REB", PerGame(totals.Rebounds), "Rebounds per game (offensive + defensive)."),
                Cell("OREB", PerGame(totals.OffensiveRebounds), "Offensive rebounds per game."),
                Cell("DREB", PerGame(totals.DefensiveRebounds), "Defensive rebounds per game."),
                Cell("AST", PerGame(totals.Assists), "Assists per game."),
                Cell("FGM-A", MadeAttempted(totals.FieldGoalsMade, totals.FieldGoalsAttempted), "Field goals made and attempted per game."),
                Cell("FG%", Pct(StatFormulas.FieldGoalPercent(totals)), "Field goal percentage: FGM / FGA."),
                Cell("3PM-A", MadeAttempted(totals.ThreePointsMade, totals.ThreePointsAttempted), "Three-pointers made and attempted per game."),
                Cell("3P%", Pct(StatFormulas.ThreePointPercent(totals)), "Three-point percentage: 3PM / 3PA."),
                Cell("FTM-A", MadeAttempted(totals.FreeThrowsMade, totals.FreeThrowsAttempted), "Free throws made and attempted per game."),
                Cell("FT%", Pct(StatFormulas.FreeThrowPercent(totals)), "Free throw percentage: FTM / FTA."),
                Cell("DD2", season.DoubleDoubles.ToString(Invariant), "Double-doubles: 10+ in two of points, rebounds, assists."),
                Cell("TD3", season.TripleDoubles.ToString(Invariant), "Triple-doubles: 10+ in points, rebounds and assists."),
            ]),
            new("Per 36 minutes", "Production scaled to a starter's minutes",
            [
                Cell("PTS", Per36(totals.Points), "Points per 36 minutes."),
                Cell("REB", Per36(totals.Rebounds), "Rebounds per 36 minutes."),
                Cell("AST", Per36(totals.Assists), "Assists per 36 minutes."),
                Cell("FGA", Per36(totals.FieldGoalsAttempted), "Field goal attempts per 36 minutes."),
                Cell("3PA", Per36(totals.ThreePointsAttempted), "Three-point attempts per 36 minutes."),
                Cell("FTA", Per36(totals.FreeThrowsAttempted), "Free throw attempts per 36 minutes."),
            ]),
            new("Shooting & efficiency", "How the points are scored",
            [
                Cell("eFG%", Pct(StatFormulas.EffectiveFieldGoal(totals)), "Effective FG%: (FGM + 0.5 × 3PM) / FGA — a three counts as one and a half twos."),
                Cell("TS%", Pct(StatFormulas.TrueShooting(totals)), "True shooting: PTS / (2 × (FGA + 0.44 × FTA)) — efficiency counting threes and free throws."),
                Cell("3PAr", Pct(StatFormulas.ThreePointRate(totals.ThreePointsAttempted, totals.FieldGoalsAttempted)), "Three-point attempt rate: 3PA / FGA."),
                Cell("FTr", Pct(StatFormulas.FreeThrowRate(totals.FreeThrowsAttempted, totals.FieldGoalsAttempted)), "Free throw rate: FTA / FGA."),
                Cell("%PTS 2PT", Pct(distribution?.TwoPoint), "Share of points from two-point field goals."),
                Cell("%PTS 3PT", Pct(distribution?.ThreePoint), "Share of points from three-pointers."),
                Cell("%PTS FT", Pct(distribution?.FreeThrow), "Share of points from free throws."),
            ]),
            new("Advanced", "On-floor shares, estimated from share of team minutes",
            [
                Cell("USG%", Pct(season.UsagePercent / 100), "Usage: the share of team plays a player finishes while on the floor (minutes-weighted)."),
                Cell("AST%", Pct(StatFormulas.AssistPercent(totals.Assists, totals.Minutes, season.Team.Minutes, season.Team.FieldGoalsMade, totals.FieldGoalsMade)), "Assist %: AST / ((MIN / (TmMIN / 5)) × TmFGM − FGM) — share of team-mates' baskets assisted while on the floor."),
                Cell("OREB%", Pct(StatFormulas.ReboundPercent(totals.OffensiveRebounds, totals.Minutes, season.Team.Minutes, season.Team.OffensiveRebounds + season.Opponent.DefensiveRebounds)), "Offensive rebound %: share of available offensive rebounds taken while on the floor."),
                Cell("DREB%", Pct(StatFormulas.ReboundPercent(totals.DefensiveRebounds, totals.Minutes, season.Team.Minutes, season.Team.DefensiveRebounds + season.Opponent.OffensiveRebounds)), "Defensive rebound %: share of available defensive rebounds taken while on the floor."),
                Cell("REB%", Pct(StatFormulas.ReboundPercent(totals.Rebounds, totals.Minutes, season.Team.Minutes, season.Team.OffensiveRebounds + season.Team.DefensiveRebounds + season.Opponent.OffensiveRebounds + season.Opponent.DefensiveRebounds)), "Rebound %: share of all available rebounds taken while on the floor."),
                Cell("+/-", Signed(StatFormulas.Scaled(totals.PlusMinus, totals, StatBasis.PerGame)), "Plus-minus per game: points scored minus points allowed while this player was on the floor."),
            ]),
        ];
    }

    /// <summary>The stated real season, grouped the way the NBA's stats site groups them.</summary>
    public static IReadOnlyList<StatGroup> StatedSeason(CareerSeasonLine season)
    {
        var m = season.Metrics ?? new Dictionary<string, double>();
        var g = season.GamesPlayed;
        string Get(string code, Func<double, string> format) => m.TryGetValue(code, out var value) ? format(value) : StatColumns.Missing;
        string PerGame(string code) => Get(code, value => Fixed(g == 0 ? null : value / g));
        string Rating(string code) => Get(code, value => value.ToString("0.0", Invariant));
        string Ratio(string code) => Get(code, value => value.ToString("0.00", Invariant));
        string Share(string code) => Get(code, value => Pct(value));
        string Rate(string code) => Get(code, value => Pct(value / 100));
        string Sign(string code) => Get(code, value => Signed(g == 0 ? null : value / g));

        var groups = new List<StatGroup>
        {
            new("Advanced", $"{season.Season} · {season.TeamName} · {g} games",
            [
                Cell("OFFRTG", Rating("OFF_RATING"), "Offensive rating: points produced per 100 possessions while on the floor."),
                Cell("DEFRTG", Rating("DEF_RATING"), "Defensive rating: points allowed per 100 possessions while on the floor."),
                Cell("NETRTG", Rating("NET_RATING"), "Net rating: OFFRTG − DEFRTG."),
                Cell("USG%", Share("USG_PCT"), "Usage: (FGA + possession-ending FTA + TOV) / possessions, while on the floor."),
                Cell("TS%", Share("TS_PCT"), "True shooting: PTS / (2 × (FGA + 0.44 × FTA))."),
                Cell("eFG%", Share("EFG_PCT"), "Effective FG%: (FGM + 0.5 × 3PM) / FGA."),
                Cell("AST%", Share("AST_PCT"), "Assist %: share of team-mates' field goals assisted while on the floor."),
                Cell("AST/TO", Ratio("AST_TO"), "Assists per turnover."),
                Cell("AST RATIO", Rating("AST_RATIO"), "Assists per 100 possessions used."),
                Cell("OREB%", Share("OREB_PCT"), "Offensive rebound %: share of available offensive rebounds taken."),
                Cell("DREB%", Share("DREB_PCT"), "Defensive rebound %: share of available defensive rebounds taken."),
                Cell("REB%", Share("REB_PCT"), "Rebound %: share of all available rebounds taken."),
                Cell("TOV%", Rate("TM_TOV_PCT"), "Turnover %: turnovers per 100 plays used."),
                Cell("PACE", Rating("PACE"), "Possessions per 48 minutes while on the floor."),
                Cell("PIE", Share("PIE"), "Player Impact Estimate: a player's share of all game events (points, rebounds, assists, steals, blocks, less misses and turnovers) while on the floor."),
            ]),
            new("Defence & ball security", "Per game",
            [
                Cell("STL", PerGame("STL"), "Steals per game."),
                Cell("BLK", PerGame("BLK"), "Blocks per game."),
                Cell("TOV", PerGame("TOV"), "Turnovers per game."),
                Cell("PF", PerGame("PF"), "Personal fouls per game."),
                Cell("+/-", Sign("PLUS_MINUS"), "Plus-minus per game: the team's point margin while he was on the floor."),
                Cell("DD2", Get("DD2", value => value.ToString("0", Invariant)), "Double-doubles."),
                Cell("TD3", Get("TD3", value => value.ToString("0", Invariant)), "Triple-doubles."),
            ]),
        };

        if (m.ContainsKey("PCT_PTS_PAINT"))
        {
            groups.Add(new("Scoring breakdown", "Where the shots and points come from",
            [
                Cell("%FGA 2PT", Share("PCT_FGA_2PT"), "Share of field goal attempts that are twos."),
                Cell("%FGA 3PT", Share("PCT_FGA_3PT"), "Share of field goal attempts that are threes."),
                Cell("%PTS 2PT", Share("PCT_PTS_2PT"), "Share of points from twos."),
                Cell("%PTS MID", Share("PCT_PTS_2PT_MR"), "Share of points from mid-range twos."),
                Cell("%PTS 3PT", Share("PCT_PTS_3PT"), "Share of points from threes."),
                Cell("%PTS FT", Share("PCT_PTS_FT"), "Share of points from free throws."),
                Cell("%PTS PAINT", Share("PCT_PTS_PAINT"), "Share of points scored in the paint."),
                Cell("%PTS FBPS", Share("PCT_PTS_FB"), "Share of points from fast breaks."),
                Cell("%PTS OFF TO", Share("PCT_PTS_OFF_TOV"), "Share of points off opponents' turnovers."),
                Cell("%FGM AST", Share("PCT_AST_FGM"), "Share of made field goals assisted by a team-mate."),
                Cell("%FGM UAST", Share("PCT_UAST_FGM"), "Share of made field goals unassisted."),
            ]));
        }

        if (m.ContainsKey("DEFLECTIONS"))
        {
            groups.Add(new("Hustle", "Per game · tracked by the league since 2015-16",
            [
                Cell("CONTESTS", PerGame("CONTESTED_SHOTS"), "Shots contested per game."),
                Cell("CONT 2PT", PerGame("CONTESTED_SHOTS_2PT"), "Two-point shots contested per game."),
                Cell("CONT 3PT", PerGame("CONTESTED_SHOTS_3PT"), "Three-point shots contested per game."),
                Cell("DEFLECTIONS", PerGame("DEFLECTIONS"), "Deflections per game: getting a hand on an opponent's pass or dribble."),
                Cell("CHARGES", Get("CHARGES_DRAWN", value => value.ToString("0", Invariant)), "Charges drawn (season total)."),
                Cell("SCREEN AST", PerGame("SCREEN_ASSISTS"), "Screen assists per game: screens that directly free a team-mate for a made shot."),
                Cell("SCREEN PTS", PerGame("SCREEN_AST_PTS"), "Points per game created by screen assists."),
                Cell("LOOSE BALLS", PerGame("LOOSE_BALLS_RECOVERED"), "Loose balls recovered per game."),
                Cell("BOX OUTS", PerGame("BOX_OUTS"), "Box outs per game."),
            ]));
        }

        return groups;
    }

    private static StatCell Cell(string label, string value, string definition) => new(label, value, definition);

    private static string Fixed(double? value) => StatColumns.Fixed(value);

    private static string Pct(double? value) => value is null ? StatColumns.Missing : StatColumns.Percent(value) + "%";

    private static string Signed(double? value) => StatColumns.Signed(value);
}
