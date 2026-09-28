using System.Globalization;
using BallGM.Application.Players;
using BallGM.Domain.Common;
using BallGM.Domain.Seasons;

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

            if (played.Count > 0)
            {
                seasonDetail = SeasonDetail(played.Select(entry => (entry.Result, entry.Line!)).ToList());
                currentSeason = new CareerSeasonLine(
                    SeasonLabel(_seasonRun.Season.Year),
                    team?.Name,
                    played.Count,
                    played.Sum(entry => entry.Line!.Minutes),
                    played.Sum(entry => entry.Line!.Points),
                    played.Sum(entry => entry.Line!.Rebounds),
                    played.Sum(entry => entry.Line!.Assists),
                    IsCurrent: true,
                    Metrics: MetricsFrom(seasonDetail));
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
    /// what the simulation counts: no steals, blocks, or plus-minus.
    /// </summary>
    private static IReadOnlyDictionary<string, double> MetricsFrom(SeasonDetailLine detail)
    {
        var metrics = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["FGM"] = detail.FieldGoalsMade,
            ["FGA"] = detail.FieldGoalsAttempted,
            ["FG3M"] = detail.ThreesMade,
            ["FG3A"] = detail.ThreesAttempted,
            ["FTM"] = detail.FreeThrowsMade,
            ["FTA"] = detail.FreeThrowsAttempted,
            ["OREB"] = detail.OffensiveRebounds,
            ["DREB"] = detail.DefensiveRebounds,
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

        Add("TS_PCT", StatFormulas.TrueShooting(detail.Points, detail.FieldGoalsAttempted, detail.FreeThrowsAttempted));
        Add("EFG_PCT", StatFormulas.EffectiveFieldGoal(detail.FieldGoalsMade, detail.ThreesMade, detail.FieldGoalsAttempted));
        Add("AST_PCT", StatFormulas.AssistPercent(detail.Assists, detail.Minutes, detail.TeamMinutes, detail.TeamFieldGoalsMade, detail.FieldGoalsMade));
        Add("REB_PCT", StatFormulas.ReboundPercent(detail.Rebounds, detail.Minutes, detail.TeamMinutes,
            detail.TeamOffensiveRebounds + detail.TeamDefensiveRebounds + detail.OpponentOffensiveRebounds + detail.OpponentDefensiveRebounds));
        return metrics;
    }

    /// <summary>A player's season totals with the team and opponent totals from the same games.</summary>
    private static SeasonDetailLine SeasonDetail(IReadOnlyList<(GameResult Result, PlayerStatLine Line)> games)
    {
        int Sum(Func<PlayerStatLine, int> pick) => games.Sum(game => pick(game.Line));
        int TeamSum(Func<PlayerStatLine, int> pick, bool opponent) => games.Sum(game =>
        {
            var teamId = opponent
                ? (game.Line.TeamId == game.Result.HomeTeamId ? game.Result.AwayTeamId : game.Result.HomeTeamId)
                : game.Line.TeamId;
            return game.Result.BoxScore!.LinesFor(teamId).Sum(pick);
        });

        bool TenOrMore(PlayerStatLine line, int categories) =>
            new[] { line.Points, line.Rebounds, line.Assists }.Count(value => value >= 10) >= categories;

        var minutes = Sum(line => line.Minutes);
        return new SeasonDetailLine(
            games.Count,
            games.Count(game => game.Line.Started),
            minutes,
            Sum(line => line.Points),
            Sum(line => line.OffensiveRebounds),
            Sum(line => line.DefensiveRebounds),
            Sum(line => line.Assists),
            Sum(line => line.FieldGoalsMade),
            Sum(line => line.FieldGoalsAttempted),
            Sum(line => line.ThreePointsMade),
            Sum(line => line.ThreePointsAttempted),
            Sum(line => line.FreeThrowsMade),
            Sum(line => line.FreeThrowsAttempted),
            games.Count(game => TenOrMore(game.Line, 2)),
            games.Count(game => TenOrMore(game.Line, 3)),
            minutes == 0 ? 0 : games.Sum(game => (double)game.Line.UsagePercent * game.Line.Minutes) / minutes,
            TeamSum(line => line.Minutes, opponent: false),
            TeamSum(line => line.FieldGoalsMade, opponent: false),
            TeamSum(line => line.OffensiveRebounds, opponent: false),
            TeamSum(line => line.DefensiveRebounds, opponent: false),
            TeamSum(line => line.OffensiveRebounds, opponent: true),
            TeamSum(line => line.DefensiveRebounds, opponent: true),
            TeamSum(line => line.Points, opponent: false),
            TeamSum(line => line.Points, opponent: true));
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
    /// it after the season run itself is cleared. Called by <see cref="ConcludeSeason"/>.
    /// </summary>
    private LeagueSnapshot WithConcludedSeason(LeagueSnapshot snapshot, SeasonRun run)
    {
        var teamOf = snapshot.Teams
            .SelectMany(team => team.PlayerIds.Select(id => (id.Value, team.Name)))
            .ToDictionary(entry => entry.Value, entry => entry.Name, StringComparer.Ordinal);

        var history = snapshot.CareerHistory.ToDictionary(entry => entry.Key, entry => entry.Value.ToList(), StringComparer.Ordinal);
        var gamesByPlayer = run.ResultsInPlayOrder
            .Where(result => result.BoxScore is not null)
            .SelectMany(result => result.BoxScore!.Lines.Select(line => (Result: result, Line: line)))
            .GroupBy(entry => entry.Line.PlayerId.Value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<(GameResult, PlayerStatLine)>)group.ToList(), StringComparer.Ordinal);
        foreach (var line in _seasonEngine.PlayerSeasonStats(run))
        {
            if (!history.TryGetValue(line.PlayerId.Value, out var seasons))
            {
                seasons = [];
                history[line.PlayerId.Value] = seasons;
            }

            seasons.Add(new CareerSeasonLine(
                SeasonLabel(run.Season.Year),
                teamOf.GetValueOrDefault(line.PlayerId.Value),
                line.GamesPlayed,
                line.TotalMinutes,
                line.TotalPoints,
                line.TotalRebounds,
                line.TotalAssists,
                Metrics: gamesByPlayer.TryGetValue(line.PlayerId.Value, out var games) ? MetricsFrom(SeasonDetail(games)) : null));
        }

        return snapshot with
        {
            CareerHistory = history.ToDictionary(entry => entry.Key, entry => (IReadOnlyList<CareerSeasonLine>)entry.Value, StringComparer.Ordinal),
        };
    }

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
            line.Minutes,
            line.Points,
            line.Rebounds,
            line.Assists,
            line.FieldGoalsMade,
            line.FieldGoalsAttempted);
    }
}
