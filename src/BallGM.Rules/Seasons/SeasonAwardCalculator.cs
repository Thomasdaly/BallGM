using System.Globalization;
using BallGM.Domain.Common;
using BallGM.Domain.Players;
using BallGM.Domain.Seasons;
using BallGM.Domain.Teams;
using BallGM.Rules.Configuration;

namespace BallGM.Rules.Seasons;

/// <summary>
/// The result of deciding one award selection: who won (or that nobody could), which selection it is
/// (1 for a single award, the team number for an all-league one), the team he finished with, the
/// deciding score in hundredths, and the finding that explains it.
/// </summary>
public sealed record AwardResult(
    string AwardCode,
    string AwardName,
    PlayerId? WinnerId,
    RuleFinding Finding,
    int Selection = 1,
    TeamId? TeamId = null,
    int ScoreHundredths = 0);

/// <summary>A player's previous regular season, for the most-improved award: games and per-game counting totals.</summary>
public sealed record PriorSeasonProduction(int Games, int Points, int Rebounds, int Assists);

/// <summary>
/// Everything an award vote reads about one concluded regular season: the games played (box scores
/// included), how many games each team was scheduled, each team's record, each player's seasons of
/// service before this one (a rookie has none), and his previous season where there was one.
/// </summary>
public sealed record AwardSeason(
    IReadOnlyList<GameResult> RegularSeasonResults,
    int SeasonGames,
    IReadOnlyDictionary<TeamId, TeamRecord> TeamRecords,
    IReadOnlyDictionary<PlayerId, int> SeasonsOfService,
    IReadOnlyDictionary<PlayerId, PriorSeasonProduction> PriorSeasons);

/// <summary>
/// Decides every configured award from one regular season. Pure and total: the same season and the
/// same <see cref="AwardRules"/> always produce the same winners.
/// <para>
/// <b>Eligibility first, then a stated score.</b> A candidate must clear the award's games floor
/// (<see cref="AwardDefinition.MinimumGamesPercent"/> of the season's games at
/// <see cref="AwardDefinition.MinimumMinutesPerGame"/> minutes or more), and the bench award also needs
/// more games off the bench than started — so a team's best player, who starts every night, can never
/// win it, however well he plays. Among the eligible, each kind ranks by its own score:
/// </para>
/// <list type="bullet">
/// <item>most valuable and all-league — game score a game, a little less for missed games, plus how
/// far his team finished above .500 (<see cref="TeamSuccessWeight"/>);</item>
/// <item>defensive and all-defensive — steals, blocks and a share of defensive rebounds a game, scaled
/// by minutes, plus how much better than the league his team defended;</item>
/// <item>bench and rookie — game score a game;</item>
/// <item>most improved — points, rebounds and assists a game against his previous season;</item>
/// <item>stat leader — the stated total.</item>
/// </list>
/// <para>
/// Ties go to the lower <see cref="PlayerId"/> ordinal. Game score is the familiar box-score linear
/// weighting, computed in integer tenths so no result depends on floating-point rounding.
/// </para>
/// </summary>
public static class SeasonAwardCalculator
{
    private const string NoStatLinesCode = "award.no_stat_lines";
    private const string NoEligibleCode = "award.no_eligible_candidate";
    private const string StatLeaderCode = "award.stat_leader";
    private const string WinnerCode = "award.winner";

    /// <summary>Game-score points (in hundredths) a team's win percentage above .500 is worth, per percentage point.</summary>
    public const int TeamSuccessWeight = 30;

    /// <summary>The share of a most-valuable score kept regardless of games missed, in per cent; the rest scales with games played.</summary>
    public const int AvailabilityFloorPercent = 85;

    /// <summary>Points allowed a game better than the league average, in hundredths of a defensive score, per point.</summary>
    public const int TeamDefenceWeight = 25;

    /// <summary>The fewest of the previous season's games, as a percentage of this season's, a most-improved candidate must have played.</summary>
    public const int MinimumPriorGamesPercent = 25;

    public static IReadOnlyList<AwardResult> Calculate(AwardSeason season, AwardRules rules)
    {
        ArgumentNullException.ThrowIfNull(season);
        ArgumentNullException.ThrowIfNull(rules);

        var players = Aggregate(season.RegularSeasonResults);
        var leagueAllowed = LeagueAverageAllowed(season.RegularSeasonResults, out var allowedByTeam);

        var results = new List<AwardResult>();
        foreach (var award in rules.Awards)
        {
            if (players.Count == 0)
            {
                results.Add(new AwardResult(
                    award.Code, award.Name, null,
                    new RuleFinding(NoStatLinesCode, $"No player recorded a stat line this season, so the {award.Name} has no winner.")));
                continue;
            }

            results.AddRange(Decide(award, season, players, leagueAllowed, allowedByTeam));
        }

        return results;
    }

    /// <summary>
    /// The pre-awards shape: every award a stat leader over season totals, with no eligibility floor.
    /// Kept for callers that hold only stat lines.
    /// </summary>
    public static IReadOnlyList<AwardResult> Calculate(IReadOnlyList<PlayerSeasonStatLine> statLines, AwardRules rules)
    {
        ArgumentNullException.ThrowIfNull(statLines);
        ArgumentNullException.ThrowIfNull(rules);

        return rules.Awards.Select(award =>
        {
            if (statLines.Count == 0 || award.StatBasis is null)
            {
                return new AwardResult(
                    award.Code, award.Name, null,
                    new RuleFinding(NoStatLinesCode, $"No player recorded a stat line this season, so the {award.Name} has no winner."));
            }

            var winner = statLines
                .OrderByDescending(line => StatFor(line.Totals, award.StatBasis.Value))
                .ThenBy(line => line.PlayerId.Value, StringComparer.Ordinal)
                .First();

            return new AwardResult(
                award.Code, award.Name, winner.PlayerId,
                new RuleFinding(StatLeaderCode, $"'{winner.PlayerId.Value}' led the league in {award.StatBasis} ({StatFor(winner.Totals, award.StatBasis.Value)}) and wins the {award.Name}."),
                ScoreHundredths: StatFor(winner.Totals, award.StatBasis.Value) * 100);
        }).ToArray();
    }

    private static IEnumerable<AwardResult> Decide(
        AwardDefinition award,
        AwardSeason season,
        IReadOnlyList<PlayerSeason> players,
        int leagueAllowed,
        IReadOnlyDictionary<TeamId, int> allowedByTeam)
    {
        var floorGames = (season.SeasonGames * award.MinimumGamesPercent + 99) / 100;

        // The most valuable player is his team's focal point: the qualified player who used the most of
        // its possessions. A great rebounder and rim protector on a winning team is a great player, but
        // real voters have always given this award to the player the offence runs through.
        var focalPoints = players
            .Where(player => player.LastTeamId is not null && player.Minutes.Count(minutes => minutes >= award.MinimumMinutesPerGame) >= floorGames)
            .GroupBy(player => player.LastTeamId!)
            .Select(team => team.Aggregate((best, player) => UsedMore(player, best) ? player : best).PlayerId)
            .ToHashSet();
        var eligible = new List<(PlayerSeason Player, int Score)>();
        var excludedByRole = 0;
        var excludedByGames = 0;

        foreach (var player in players)
        {
            var qualifying = player.Minutes.Count(minutes => minutes >= award.MinimumMinutesPerGame);
            if (qualifying < floorGames)
            {
                excludedByGames++;
                continue;
            }

            var score = award.Kind switch
            {
                AwardKind.MostValuable => focalPoints.Contains(player.PlayerId) ? ValueScore(player, season) : null,
                AwardKind.AllLeague => ValueScore(player, season),
                AwardKind.Defensive or AwardKind.AllDefensive => DefensiveScore(player, leagueAllowed, allowedByTeam),
                AwardKind.Bench => player.BenchGames > player.Totals.Starts ? GameScoreHundredths(player) : (int?)null,
                AwardKind.Rookie => season.SeasonsOfService.GetValueOrDefault(player.PlayerId) == 0 ? GameScoreHundredths(player) : null,
                AwardKind.MostImproved => ImprovementScore(player, season),
                _ => award.StatBasis is { } basis ? StatFor(player.Totals, basis) * 100 : null,
            };

            if (score is null)
            {
                excludedByRole++;
                continue;
            }

            eligible.Add((player, score.Value));
        }

        var ranked = eligible
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Player.PlayerId.Value, StringComparer.Ordinal)
            .ToList();

        var eligibility = EligibilityText(award, floorGames, season.SeasonGames, eligible.Count, excludedByGames, excludedByRole);
        if (ranked.Count == 0)
        {
            yield return new AwardResult(
                award.Code, award.Name, null,
                new RuleFinding(NoEligibleCode, $"Nobody was eligible for the {award.Name}: {eligibility}"));
            yield break;
        }

        var selections = award.Kind is AwardKind.AllLeague or AwardKind.AllDefensive
            ? Math.Min(ranked.Count, award.Teams * 5)
            : 1;

        for (var index = 0; index < selections; index++)
        {
            var (player, score) = ranked[index];
            var selection = award.Kind is AwardKind.AllLeague or AwardKind.AllDefensive ? (index / 5) + 1 : 1;
            yield return new AwardResult(
                award.Code,
                award.Name,
                player.PlayerId,
                new RuleFinding(WinnerCode, $"{Describe(award, player, score, season, leagueAllowed, allowedByTeam)} {eligibility}"),
                selection,
                player.LastTeamId,
                score);
        }
    }

    /// <summary>Game score a game, in hundredths.</summary>
    private static int GameScoreHundredths(PlayerSeason player) =>
        player.Totals.Games == 0 ? 0 : (int)(GameScoreTenths(player.Totals) * 10 / player.Totals.Games);

    /// <summary>Season game score in tenths: the box-score linear weighting, integer.</summary>
    private static long GameScoreTenths(CountingStats totals) =>
        (10L * totals.Points) + (4L * totals.FieldGoalsMade) - (7L * totals.FieldGoalsAttempted)
        - (4L * (totals.FreeThrowsAttempted - totals.FreeThrowsMade)) + (7L * totals.OffensiveRebounds)
        + (3L * totals.DefensiveRebounds) + (10L * totals.Steals) + (7L * totals.Assists) + (7L * totals.Blocks)
        - (4L * totals.PersonalFouls) - (10L * totals.Turnovers);

    private static int ValueScore(PlayerSeason player, AwardSeason season)
    {
        var gameScore = GameScoreHundredths(player);
        var gamesPercent = season.SeasonGames == 0 ? 100 : Math.Min(100, player.Totals.Games * 100 / season.SeasonGames);
        var availability = AvailabilityFloorPercent + ((100 - AvailabilityFloorPercent) * gamesPercent / 100);
        return (gameScore * availability / 100) + TeamSuccess(player, season);
    }

    /// <summary>His team's win percentage above (or below) .500, in game-score hundredths.</summary>
    private static int TeamSuccess(PlayerSeason player, AwardSeason season)
    {
        if (player.LastTeamId is null || !season.TeamRecords.TryGetValue(player.LastTeamId, out var record) || record.Wins + record.Losses == 0)
        {
            return 0;
        }

        var winBasisPoints = record.Wins * 10_000 / (record.Wins + record.Losses);
        return TeamSuccessWeight * (winBasisPoints - 5_000) / 100;
    }

    private static int DefensiveScore(PlayerSeason player, int leagueAllowed, IReadOnlyDictionary<TeamId, int> allowedByTeam)
    {
        var totals = player.Totals;
        if (totals.Games == 0)
        {
            return 0;
        }

        // Stocks and a third of a defensive board a game, in hundredths, scaled by minutes a game
        // (a full 36 counts whole), then how much better than the league his team defended.
        var activity = ((300L * totals.Steals) + (300L * totals.Blocks) + (100L * totals.DefensiveRebounds)) / 3 / totals.Games;
        var minutesScale = Math.Min(36L, totals.Minutes / totals.Games);
        var team = player.LastTeamId is not null && allowedByTeam.TryGetValue(player.LastTeamId, out var allowed)
            ? TeamDefenceWeight * (leagueAllowed - allowed) / 100
            : 0;
        return (int)(activity * minutesScale / 36) + team;
    }

    private static int? ImprovementScore(PlayerSeason player, AwardSeason season)
    {
        if (!season.PriorSeasons.TryGetValue(player.PlayerId, out var prior) ||
            prior.Games * 100 < season.SeasonGames * MinimumPriorGamesPercent || player.Totals.Games == 0)
        {
            return null;
        }

        var now = (player.Totals.Points + player.Totals.Rebounds + player.Totals.Assists) * 100 / player.Totals.Games;
        var before = (prior.Points + prior.Rebounds + prior.Assists) * 100 / prior.Games;
        return now - before;
    }

    private static string Describe(
        AwardDefinition award,
        PlayerSeason player,
        int score,
        AwardSeason season,
        int leagueAllowed,
        IReadOnlyDictionary<TeamId, int> allowedByTeam)
    {
        var totals = player.Totals;
        var games = Math.Max(1, totals.Games);
        string PerGame(int value) => (value / (double)games).ToString("0.0", CultureInfo.InvariantCulture);
        string Hundredths(int value) => (value / 100.0).ToString("0.0", CultureInfo.InvariantCulture);
        var line = $"{PerGame(totals.Points)} points, {PerGame(totals.Rebounds)} rebounds and {PerGame(totals.Assists)} assists a game";
        var record = player.LastTeamId is not null && season.TeamRecords.TryGetValue(player.LastTeamId, out var teamRecord)
            ? $" for a {teamRecord.Wins}-{teamRecord.Losses} team"
            : string.Empty;

        return award.Kind switch
        {
            AwardKind.MostValuable or AwardKind.AllLeague =>
                $"Averaged {line} (a {Hundredths(GameScoreHundredths(player))} game score){record}; value score {Hundredths(score)}.",
            AwardKind.Defensive or AwardKind.AllDefensive =>
                $"Averaged {PerGame(totals.Steals)} steals, {PerGame(totals.Blocks)} blocks and {PerGame(totals.DefensiveRebounds)} defensive rebounds in {PerGame(totals.Minutes)} minutes{record} that allowed {Hundredths(allowedByTeam.GetValueOrDefault(player.LastTeamId!))} points a game (league {Hundredths(leagueAllowed)}); defensive score {Hundredths(score)}.",
            AwardKind.Bench =>
                $"Came off the bench in {player.BenchGames} of {totals.Games} games and averaged {line} (a {Hundredths(score)} game score).",
            AwardKind.Rookie =>
                $"In his first season, averaged {line} (a {Hundredths(score)} game score).",
            AwardKind.MostImproved =>
                $"Averaged {line}, {Hundredths(score)} more points, rebounds and assists a game than the season before.",
            _ => $"Led the league in {award.StatBasis} ({Hundredths(score)}).",
        };
    }

    private static string EligibilityText(AwardDefinition award, int floorGames, int seasonGames, int eligible, int excludedByGames, int excludedByRole)
    {
        var floor = award.MinimumGamesPercent == 0
            ? "every player who appeared was considered"
            : $"candidates needed {floorGames} of {seasonGames} games{(award.MinimumMinutesPerGame > 0 ? $" of {award.MinimumMinutesPerGame}+ minutes" : string.Empty)}";
        var role = award.Kind switch
        {
            AwardKind.Bench => $"; {excludedByRole} more started at least as often as they came off the bench",
            AwardKind.Rookie => $"; {excludedByRole} more were not in their first season",
            AwardKind.MostImproved => $"; {excludedByRole} more had no comparable previous season",
            AwardKind.MostValuable => $"; {excludedByRole} more were not the player their team's offence ran through",
            _ => string.Empty,
        };
        return $"Eligibility: {floor} ({excludedByGames} fell short){role}; {eligible} eligible.";
    }

    /// <summary>
    /// Whether this player used more possessions a game than that one (shots, 0.44 of a trip to the line,
    /// turnovers), compared exactly — cross-multiplied rather than rounded per game, so two players a
    /// fraction apart are never put level and split by identifier. A true tie goes to the lower identifier.
    /// </summary>
    private static bool UsedMore(PlayerSeason player, PlayerSeason than)
    {
        static long Used(CountingStats totals) => (totals.FieldGoalsAttempted * 100L) + (totals.FreeThrowsAttempted * 44L) + (totals.Turnovers * 100L);
        var left = Used(player.Totals) * Math.Max(1, than.Totals.Games);
        var right = Used(than.Totals) * Math.Max(1, player.Totals.Games);
        return left != right ? left > right : string.CompareOrdinal(player.PlayerId.Value, than.PlayerId.Value) < 0;
    }

    private static int StatFor(CountingStats totals, AwardStatBasis basis) => basis switch
    {
        AwardStatBasis.TotalPoints => totals.Points,
        AwardStatBasis.TotalRebounds => totals.Rebounds,
        AwardStatBasis.TotalAssists => totals.Assists,
        AwardStatBasis.TotalMinutes => totals.Minutes,
        _ => throw new ArgumentOutOfRangeException(nameof(basis), basis, "Unknown award stat basis."),
    };

    /// <summary>Points each team allowed a game, in hundredths, and the league's average of the same.</summary>
    private static int LeagueAverageAllowed(IReadOnlyList<GameResult> results, out Dictionary<TeamId, int> allowedByTeam)
    {
        var allowed = new Dictionary<TeamId, (long Points, int Games)>();
        void Add(TeamId team, int points)
        {
            var (total, games) = allowed.GetValueOrDefault(team);
            allowed[team] = (total + points, games + 1);
        }

        foreach (var result in results)
        {
            Add(result.HomeTeamId, result.AwayPoints);
            Add(result.AwayTeamId, result.HomePoints);
        }

        allowedByTeam = allowed.ToDictionary(entry => entry.Key, entry => (int)(entry.Value.Points * 100 / Math.Max(1, entry.Value.Games)));
        return allowedByTeam.Count == 0 ? 0 : (int)allowedByTeam.Values.Average();
    }

    /// <summary>One player's regular season as the votes read it, built from the box scores in play order.</summary>
    private static List<PlayerSeason> Aggregate(IReadOnlyList<GameResult> results)
    {
        var byPlayer = new Dictionary<string, PlayerSeason>(StringComparer.Ordinal);
        foreach (var result in results)
        {
            if (result.Phase != SeasonPhase.RegularSeason || result.BoxScore is null)
            {
                continue;
            }

            foreach (var line in result.BoxScore.Lines)
            {
                if (!byPlayer.TryGetValue(line.PlayerId.Value, out var season))
                {
                    season = new PlayerSeason(line.PlayerId);
                    byPlayer[line.PlayerId.Value] = season;
                }

                season.Add(line);
            }
        }

        return byPlayer.Values.OrderBy(season => season.PlayerId.Value, StringComparer.Ordinal).ToList();
    }

    private sealed class PlayerSeason(PlayerId playerId)
    {
        public PlayerId PlayerId { get; } = playerId;

        public CountingStats Totals { get; private set; } = CountingStats.None;

        public List<int> Minutes { get; } = [];

        public int BenchGames { get; private set; }

        /// <summary>The team he played his last game for: where a traded player finished the season.</summary>
        public TeamId? LastTeamId { get; private set; }

        public void Add(PlayerStatLine line)
        {
            Totals += CountingStats.Of(line);
            Minutes.Add(line.Minutes);
            BenchGames += line.Started ? 0 : 1;
            LastTeamId = line.TeamId;
        }
    }
}
