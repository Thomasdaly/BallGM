using BallGM.Domain.Common;
using BallGM.Domain.Contracts;
using BallGM.Domain.Leagues;
using BallGM.Domain.Players;
using BallGM.Domain.Seasons;
using BallGM.Domain.Teams;
using BallGM.Rules.Configuration;

namespace BallGM.Rules.Seasons;

/// <summary>What concluding one finished season changed.</summary>
public sealed record ConcludedSeason(
    SeasonHistoryEntry Entry,
    IReadOnlyList<PlayerId> PlayersReleasedToFreeAgency,
    int PlayersCreditedService,
    IReadOnlyList<RuleFinding> Notes);

/// <summary>
/// Turns a finished <see cref="SeasonRun"/> into the offseason: the champion and the final table
/// archived on <see cref="League"/>, service time credited to everyone who was rostered through it,
/// and every contract whose last season has elapsed released back into the free-agent pool.
/// <para>
/// <b>Validated, then applied — no rollback machinery.</b> Both unhappy paths this method refuses (an
/// unfinished season, a season already concluded) are read-only checks against state that cannot
/// change between the check and the mutation that follows — unlike a trade or a signing, which
/// re-validates because time may pass between assessing and submitting a proposal built earlier.
/// Concluding a season has no such gap: it is one call, so once validation passes, every mutation
/// that follows is against state this method just proved was safe to mutate and cannot fail. There is
/// therefore no assess/execute split and no captured restore point here, deliberately — see
/// <c>docs/architecture.md</c> → "The season boundary" for why this reads the stated rollback
/// requirement narrowly rather than building generic capture/restore machinery nothing can trigger.
/// </para>
/// <para>
/// <b>The champion is re-derived, never stored.</b> Nothing on <see cref="SeasonRun"/> keeps the
/// postseason bracket's winner once the bracket stops needing new fixtures. <see cref="PostseasonBracketBuilder.DrawFor"/>
/// is pure and fully re-derivable from the finished season's own table and results, so this asks it
/// once more against the final day rather than plumbing new state through the engine that plays the
/// season.
/// </para>
/// </summary>
public sealed class SeasonConclusion
{
    private const string IncompleteSeasonCode = "season.conclusion_of_incomplete_season";
    private const string AlreadyConcludedCode = "season.already_concluded";
    private const string ChampionUndeterminedCode = "season.concluded_without_a_champion";

    private readonly StandingsCalculator _standingsCalculator = new();
    private readonly PostseasonBracketBuilder _bracketBuilder = new();

    public DomainOperationResult<ConcludedSeason> Conclude(
        League league,
        IReadOnlyDictionary<TeamId, string> teamNames,
        IReadOnlyCollection<Team> teams,
        IReadOnlyCollection<Player> players,
        IReadOnlyCollection<Contract> contracts,
        SeasonRun run,
        StandingsRules standingsRules,
        PostseasonRules postseasonRules)
    {
        ArgumentNullException.ThrowIfNull(league);
        ArgumentNullException.ThrowIfNull(teamNames);
        ArgumentNullException.ThrowIfNull(teams);
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(contracts);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(standingsRules);
        ArgumentNullException.ThrowIfNull(postseasonRules);

        if (!run.IsComplete)
        {
            return DomainOperationResult<ConcludedSeason>.Failure(new DomainError(
                IncompleteSeasonCode,
                $"Season {run.Season.Year} has reached {run.CurrentDay} of {run.Calendar.LengthInDays} days and cannot be concluded until it is played out."));
        }

        if (league.History.Any(entry => entry.Season.Year == run.Season.Year))
        {
            return DomainOperationResult<ConcludedSeason>.Failure(new DomainError(
                AlreadyConcludedCode,
                $"Season {run.Season.Year} has already been concluded and archived in this league's history."));
        }

        var standings = _standingsCalculator.Calculate(league, teamNames, run.Results, standingsRules);
        var notes = new List<RuleFinding>();
        var championId = DetermineChampion(league, standings, postseasonRules, run, notes);

        var finalStandings = standings.Rows
            .Select((row, index) => new SeasonHistoryTeamRecord(row.TeamId, index + 1, row.Overall, row.PointsFor, row.PointsAgainst))
            .ToList();

        var entry = new SeasonHistoryEntry(run.Season, championId, finalStandings);

        // Read-only work is over. Everything below mutates, and nothing below can fail: every step
        // acts on state this method just proved was valid a moment ago, and nothing else could have
        // changed it in between.
        var recordResult = league.RecordSeason(entry);
        if (recordResult.IsFailure)
        {
            return DomainOperationResult<ConcludedSeason>.Failure(recordResult.Errors.ToArray());
        }

        var playersById = players.ToDictionary(player => player.Id, player => player);
        var rosteredPlayerIds = teams.SelectMany(team => team.PlayerIds).Distinct().ToList();

        foreach (var playerId in rosteredPlayerIds)
        {
            if (playersById.TryGetValue(playerId, out var player))
            {
                player.CompleteSeasonOfService();
            }
        }

        var teamsById = teams.ToDictionary(team => team.Id, team => team);
        var released = new List<PlayerId>();

        foreach (var contract in contracts)
        {
            if (contract.IsTerminated || contract.LastSeason.Year > run.Season.Year)
            {
                continue;
            }

            if (!teamsById.TryGetValue(contract.TeamId, out var team) || !team.PlayerIds.Contains(contract.PlayerId))
            {
                continue;
            }

            team.ReleaseExpiredPlayer(contract.PlayerId);
            released.Add(contract.PlayerId);
        }

        return DomainOperationResult<ConcludedSeason>.Success(new ConcludedSeason(
            entry, released, rosteredPlayerIds.Count, notes));
    }

    /// <summary>
    /// Re-derives the postseason winner from the finished season's own table and results, or
    /// <c>null</c> where there is nobody to crown — a league with no postseason, or (a warning rather
    /// than a violation, since the regular season is unaffected) a bracket the reserved days did not
    /// leave time to finish.
    /// </summary>
    private TeamId? DetermineChampion(
        League league,
        Standings standings,
        PostseasonRules postseasonRules,
        SeasonRun run,
        List<RuleFinding> notes)
    {
        if (!postseasonRules.IsConfigured)
        {
            return null;
        }

        var seedingResult = _bracketBuilder.Seed(league, standings, postseasonRules);
        if (seedingResult.IsFailure)
        {
            notes.Add(new RuleFinding(
                ChampionUndeterminedCode,
                $"Season {run.Season.Year}'s postseason bracket could not be re-seeded from its final table, so no champion was recorded: {string.Join("; ", seedingResult.Errors.Select(error => error.Message))}"));
            return null;
        }

        var drawResult = _bracketBuilder.DrawFor(
            run.Season,
            seedingResult.Value,
            postseasonRules,
            run.Calendar,
            run.Schedule,
            run.Results,
            run.Calendar.LastDay);

        if (drawResult.IsFailure || !drawResult.Value.IsComplete)
        {
            notes.Add(new RuleFinding(
                ChampionUndeterminedCode,
                $"Season {run.Season.Year}'s calendar ran out before its postseason bracket finished, so no champion was recorded."));
            return null;
        }

        return drawResult.Value.ChampionId;
    }
}
