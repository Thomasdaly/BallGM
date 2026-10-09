using System.Globalization;
using BallGM.Application.Players;
using BallGM.Domain.Common;
using BallGM.Domain.Seasons;
using BallGM.Domain.Teams;

namespace BallGM.Application.Leagues;

/// <summary>The player half of <see cref="LeagueSession"/>: the profile a squad screen opens.</summary>
public sealed partial class LeagueSession
{
    private const string ProfileUnknownPlayerCode = "player_profile.unknown_player";

    /// <summary>How many recent games the profile's form guide shows.</summary>
    public const int RecentGameCount = 5;

    public DomainOperationResult<PlayerProfileSummary> PlayerProfile(string playerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerId);

        if (_snapshot is null)
        {
            return NotLoaded<PlayerProfileSummary>();
        }

        var player = _snapshot.Players.FirstOrDefault(candidate => candidate.Id.Value == playerId);
        if (player is null)
        {
            return DomainOperationResult<PlayerProfileSummary>.Failure(
                new DomainError(ProfileUnknownPlayerCode, $"No player '{playerId}' is in this league."));
        }

        var team = _snapshot.Teams.FirstOrDefault(candidate => candidate.PlayerIds.Contains(player.Id));
        var teamNames = TeamNames(_snapshot);
        var asOf = _seasonRun is null
            ? new DateOnly(_snapshot.CurrentSeason.Year, 7, 1)
            : _seasonRun.Calendar.DateOn(_seasonRun.CurrentDay);

        // Every live contract from now on, in order: the current deal and any extension that follows it.
        var liveContracts = _snapshot.Contracts
            .Where(candidate => candidate.PlayerId == player.Id && !candidate.IsTerminated && candidate.LastSeason.Year >= _snapshot.CurrentSeason.Year)
            .OrderBy(candidate => candidate.FirstSeason.Year)
            .ToList();
        var contractLines = liveContracts
            .SelectMany(contract => contract.Terms)
            .Where(term => term.Season.Year >= _snapshot.CurrentSeason.Year)
            .Select(term => new ContractSeasonLine(term.Season.Year, term.Compensation.SmallestUnits, term.IsPendingOption))
            .ToList();

        var recentGames = new List<RecentGameLine>();
        CareerSeasonLine? currentSeason = null;
        SeasonDetailLine? seasonDetail = null;
        if (_seasonRun is not null)
        {
            var played = _seasonRun.ResultsInPlayOrder
                .Select(result => (Result: result, Line: result.BoxScore?.Lines.FirstOrDefault(line => line.PlayerId == player.Id)))
                .Where(entry => entry.Line is not null)
                .ToList();

            recentGames = played
                .TakeLast(RecentGameCount)
                .Reverse()
                .Select(entry => ToRecentGame(entry.Result, entry.Line!, teamNames, _seasonRun))
                .ToList();

            // Recent form is every game, playoffs included; the season line is the regular season only.
            var regularSeason = played
                .Where(entry => entry.Result.Phase == SeasonPhase.RegularSeason)
                .Select(entry => (entry.Result, entry.Line!))
                .ToList();
            if (regularSeason.Count > 0)
            {
                seasonDetail = SeasonDetail(regularSeason);
                currentSeason = CareerLine(_seasonRun.Season.Year, team?.Name, seasonDetail, SeasonPhase.RegularSeason, isCurrent: true);
            }
        }

        var career = _snapshot.CareerHistory.GetValueOrDefault(player.Id.Value) ?? [];
        var rating = player.Rating;

        return DomainOperationResult<PlayerProfileSummary>.Success(new PlayerProfileSummary(
            player.Id.Value,
            player.FullName,
            GetLeagueOverviewQuery.DescribePosition(player.Position),
            player.AgeOn(asOf),
            player.BirthDate,
            player.SeasonsOfService,
            rating.Overall,
            new PlayerAttributes(rating.Height, rating.Speed, rating.Strength, rating.Passing, rating.LateralQuickness),
            team?.Name,
            _snapshot.Artwork.PortraitFor(player.Id),
            player.IsInjured,
            player.CurrentInjury?.Description,
            contractLines,
            currentSeason,
            recentGames,
            currentSeason is null ? career : [.. career, currentSeason],
            seasonDetail,
            ContractDetail(liveContracts, player.SeasonsOfService)));
    }

    /// <summary>
    /// A simulated season as career metrics, under the same stat codes a data pack uses (FGM, TS_PCT,
    /// USG_PCT, ...), so a played season and a stated one read the same in the career table. Only
    /// what the simulation counts.
    /// </summary>
    private static IReadOnlyDictionary<string, double> MetricsFrom(SeasonDetailLine detail)
    {
        var metrics = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["FGM"] = detail.Totals.FieldGoalsMade,
            ["FGA"] = detail.Totals.FieldGoalsAttempted,
            ["FG3M"] = detail.Totals.ThreePointsMade,
            ["FG3A"] = detail.Totals.ThreePointsAttempted,
            ["FTM"] = detail.Totals.FreeThrowsMade,
            ["FTA"] = detail.Totals.FreeThrowsAttempted,
            ["OREB"] = detail.Totals.OffensiveRebounds,
            ["DREB"] = detail.Totals.DefensiveRebounds,
            ["PLUS_MINUS"] = detail.Totals.PlusMinus,
            ["DD2"] = detail.DoubleDoubles,
            ["TD3"] = detail.TripleDoubles,
            ["USG_PCT"] = detail.UsagePercent / 100,
        };

        void Add(string code, double? value)
        {
            if (value is { } present)
            {
                metrics[code] = present;
            }
        }

        Add("TS_PCT", StatFormulas.TrueShooting(detail.Totals));
        Add("EFG_PCT", StatFormulas.EffectiveFieldGoal(detail.Totals));
        Add("AST_PCT", StatFormulas.AssistPercent(detail.Totals.Assists, detail.Totals.Minutes, detail.Team.Minutes, detail.Team.FieldGoalsMade, detail.Totals.FieldGoalsMade));
        Add("REB_PCT", StatFormulas.ReboundPercent(detail.Totals.Rebounds, detail.Totals.Minutes, detail.Team.Minutes,
            detail.Team.OffensiveRebounds + detail.Team.DefensiveRebounds + detail.Opponent.OffensiveRebounds + detail.Opponent.DefensiveRebounds));
        return metrics;
    }

    /// <summary>
    /// A player's season totals with the team and opponent totals from the same games — every
    /// counting stat on each, as <see cref="CountingStats"/>, so nothing the engine counts is dropped
    /// here. Team and opponent totals come from <see cref="BoxScore.TotalsFor"/>, one per game.
    /// </summary>
    private static SeasonDetailLine SeasonDetail(IReadOnlyList<(GameResult Result, PlayerStatLine Line)> games)
    {
        static TeamId OpponentOf(GameResult result, TeamId teamId) => teamId == result.HomeTeamId ? result.AwayTeamId : result.HomeTeamId;

        bool TenOrMore(PlayerStatLine line, int categories) =>
            new[] { line.Points, line.Rebounds, line.Assists }.Count(value => value >= 10) >= categories;

        var totals = CountingStats.Sum(games.Select(game => game.Line));
        return new SeasonDetailLine(
            totals,
            CountingStats.Sum(games.Select(game => game.Result.BoxScore!.TotalsFor(game.Line.TeamId))),
            CountingStats.Sum(games.Select(game => game.Result.BoxScore!.TotalsFor(OpponentOf(game.Result, game.Line.TeamId)))),
            games.Count(game => TenOrMore(game.Line, 2)),
            games.Count(game => TenOrMore(game.Line, 3)),
            totals.Minutes == 0 ? 0 : games.Sum(game => (double)game.Line.UsagePercent * game.Line.Minutes) / totals.Minutes);
    }

    /// <summary>The contract seen against the league's scales: share of the cap, place between min and max, free agency.</summary>
    private ContractDetailLine? ContractDetail(IReadOnlyList<Domain.Contracts.Contract> contracts, int seasonsOfService)
    {
        var configuration = _snapshot!.Configuration;
        var softCap = configuration.SoftCap?.SmallestUnits;
        var ceilingPercent = configuration.Negotiation.CompensationCeilingTiers.ValueFor(seasonsOfService);
        var floor = configuration.Negotiation.CompensationFloorScale.ValueFor(seasonsOfService);

        var terms = contracts.SelectMany(contract => contract.Terms).Where(term => term.Season.Year >= _snapshot.CurrentSeason.Year).ToList();
        var years = terms
            .Select(term => new ContractYearLine(
                term.Season.Year,
                term.Compensation.SmallestUnits,
                term.GuaranteedAmount.SmallestUnits,
                term.Option switch
                {
                    Domain.Contracts.ContractOptionKind.Player => "Player",
                    Domain.Contracts.ContractOptionKind.Team => "Team",
                    _ => null,
                },
                softCap is > 0 ? (double)term.Compensation.SmallestUnits / softCap.Value : null))
            .ToList();

        var total = years.Sum(year => year.Salary);
        return new ContractDetailLine(
            softCap,
            softCap is { } cap && ceilingPercent is { } percent ? cap * percent / 100 : null,
            ceilingPercent is { } p ? (int)p : null,
            floor,
            total,
            years.Sum(year => year.Guaranteed),
            years.Count == 0 ? 0 : total / years.Count,
            years.Count == 0 ? null : years[^1].Season + 1,
            years);
    }

    /// <summary>"2026-27" for a season starting in 2026 — the way basketball seasons are named.</summary>
    public static string SeasonLabel(int startYear) =>
        string.Create(CultureInfo.InvariantCulture, $"{startYear}-{(startYear + 1) % 100:00}");

    /// <summary>
    /// Folds a season about to be concluded into every player's career history, so the profile keeps
    /// it after the season run itself is cleared: one regular-season row for everyone who played, and
    /// a separate playoff row for everyone who reached the postseason. Called by <see cref="ConcludeSeason"/>.
    /// </summary>
    private static LeagueSnapshot WithConcludedSeason(LeagueSnapshot snapshot, SeasonRun run)
    {
        var teamOf = snapshot.Teams
            .SelectMany(team => team.PlayerIds.Select(id => (id.Value, team.Name)))
            .ToDictionary(entry => entry.Value, entry => entry.Name, StringComparer.Ordinal);

        var history = snapshot.CareerHistory.ToDictionary(entry => entry.Key, entry => entry.Value.ToList(), StringComparer.Ordinal);

        foreach (var phase in new[] { SeasonPhase.RegularSeason, SeasonPhase.Postseason })
        {
            var gamesByPlayer = run.ResultsInPlayOrder
                .Where(result => result.Phase == phase && result.BoxScore is not null)
                .SelectMany(result => result.BoxScore!.Lines.Select(line => (Result: result, Line: line)))
                .GroupBy(entry => entry.Line.PlayerId.Value, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal);

            foreach (var games in gamesByPlayer)
            {
                if (!history.TryGetValue(games.Key, out var seasons))
                {
                    seasons = [];
                    history[games.Key] = seasons;
                }

                seasons.Add(CareerLine(run.Season.Year, teamOf.GetValueOrDefault(games.Key), SeasonDetail(games.ToList()), phase, isCurrent: false));
            }
        }

        return snapshot with
        {
            CareerHistory = history.ToDictionary(entry => entry.Key, entry => (IReadOnlyList<CareerSeasonLine>)entry.Value, StringComparer.Ordinal),
        };
    }

    /// <summary>One season of one phase as a career row: the live profile line and a concluded one are built the same way.</summary>
    private static CareerSeasonLine CareerLine(int seasonYear, string? teamName, SeasonDetailLine detail, SeasonPhase phase, bool isCurrent) =>
        new(
            SeasonLabel(seasonYear),
            teamName,
            detail.Totals.Games,
            detail.Totals.Minutes,
            detail.Totals.Points,
            detail.Totals.Rebounds,
            detail.Totals.Assists,
            isCurrent,
            MetricsFrom(detail),
            IsPostseason: phase == SeasonPhase.Postseason);

    private static RecentGameLine ToRecentGame(
        GameResult result,
        PlayerStatLine line,
        IReadOnlyDictionary<Domain.Teams.TeamId, string> teamNames,
        SeasonRun run)
    {
        var isHome = line.TeamId == result.HomeTeamId;
        var opponentId = isHome ? result.AwayTeamId : result.HomeTeamId;
        var teamPoints = isHome ? result.HomePoints : result.AwayPoints;
        var opponentPoints = isHome ? result.AwayPoints : result.HomePoints;

        return new RecentGameLine(
            result.Day.Index,
            run.Calendar.DateOn(result.Day).ToString("MMM d", CultureInfo.InvariantCulture),
            teamNames.GetValueOrDefault(opponentId, opponentId.Value),
            isHome,
            teamPoints > opponentPoints,
            teamPoints,
            opponentPoints,
            CountingStats.Of(line));
    }
}
