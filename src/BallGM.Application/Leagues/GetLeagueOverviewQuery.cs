using System.Globalization;
using BallGM.Application.Cap;
using BallGM.Application.DraftAssets;
using BallGM.Application.Negotiations;
using BallGM.Domain.Cap;
using BallGM.Domain.Common;
using BallGM.Domain.Contracts;
using BallGM.Domain.DraftAssets;
using BallGM.Domain.Franchises;
using BallGM.Domain.Leagues;
using BallGM.Domain.Players;
using BallGM.Domain.Teams;
using BallGM.Domain.Transactions;

namespace BallGM.Application.Leagues;

/// <summary>
/// Reads a loaded league through <see cref="ILeagueDataSource"/> and flattens it into the
/// <see cref="LeagueOverview"/> read model the client renders. Aggregates reference each other by
/// identifier rather than by object graph, so resolving those references — and reporting a dangling
/// one as an explainable failure instead of a crash — is this query's real work.
/// <para>
/// Cap figures are projected from the loaded contracts and evaluated through
/// <see cref="ICapLedger"/>; this query never adds up money itself, so the screen and the rules
/// layer can never disagree about a payroll.
/// </para>
/// </summary>
public sealed class GetLeagueOverviewQuery(
    ILeagueDataSource dataSource,
    ICapLedger capLedger,
    IDraftAssetLedger draftAssetLedger,
    ISigningEngine signingEngine)
{
    private const string UnknownTeamCode = "league_overview.unknown_team";
    private const string UnknownFranchiseCode = "league_overview.unknown_franchise";
    private const string UnknownPlayerCode = "league_overview.unknown_player";

    /// <summary>How much of the ledger the cap sheet shows. The full history is a later milestone.</summary>
    private const int RecentTransactionCount = 8;

    private readonly ILeagueDataSource _dataSource =
        dataSource ?? throw new ArgumentNullException(nameof(dataSource));

    private readonly ICapLedger _capLedger =
        capLedger ?? throw new ArgumentNullException(nameof(capLedger));

    private readonly IDraftAssetLedger _draftAssetLedger =
        draftAssetLedger ?? throw new ArgumentNullException(nameof(draftAssetLedger));

    private readonly ISigningEngine _signingEngine =
        signingEngine ?? throw new ArgumentNullException(nameof(signingEngine));

    public DomainOperationResult<LeagueOverview> Execute()
    {
        var snapshotResult = _dataSource.Load();
        return snapshotResult.IsFailure
            ? DomainOperationResult<LeagueOverview>.Failure(snapshotResult.Errors.ToArray())
            : Project(snapshotResult.Value);
    }

    /// <summary>
    /// Projects an already-loaded league. Separate from <see cref="Execute"/> so a session that holds
    /// a league in memory can re-project it after a trade without reloading from disk — reloading
    /// would throw away the very change the screen is meant to show.
    /// </summary>
    public DomainOperationResult<LeagueOverview> Project(LeagueSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var teamsById = snapshot.Teams.ToDictionary(team => team.Id);
        var franchisesById = snapshot.Franchises.ToDictionary(franchise => franchise.Id);
        var playersById = snapshot.Players.ToDictionary(player => player.Id);

        var errors = new List<DomainError>();
        var teamSummaries = new List<TeamSummary>();

        foreach (var teamId in snapshot.League.TeamIds.OrderBy(id => id.Value, StringComparer.Ordinal))
        {
            if (!teamsById.TryGetValue(teamId, out var team))
            {
                errors.Add(new DomainError(
                    UnknownTeamCode,
                    $"League '{snapshot.League.Name}' references team '{teamId.Value}', which was not loaded."));
                continue;
            }

            var franchiseName = ResolveFranchiseName(team, franchisesById, errors);
            var capSheetResult = BuildCapSummary(snapshot, team, playersById);
            if (capSheetResult.IsFailure)
            {
                errors.AddRange(capSheetResult.Errors);
                continue;
            }

            var roster = ResolveRoster(team, snapshot, playersById, errors);

            teamSummaries.Add(new TeamSummary(
                team.Id.Value,
                team.Name,
                franchiseName,
                team.RosterCount,
                roster,
                capSheetResult.Value));
        }

        var pickBoardResult = BuildPickBoard(snapshot, franchisesById);
        if (pickBoardResult.IsFailure)
        {
            errors.AddRange(pickBoardResult.Errors);
        }

        if (errors.Count > 0)
        {
            return DomainOperationResult<LeagueOverview>.Failure(errors.ToArray());
        }

        // League membership carries no order of its own, and identifiers are minted per load, so
        // sorting by identifier reshuffled the team list on every launch. Standings and divisions
        // are later milestones; until then the read model presents teams by name.
        teamSummaries = teamSummaries
            .OrderBy(team => team.TeamName, StringComparer.Ordinal)
            .ToList();

        var configuration = snapshot.Configuration;
        var overview = new LeagueOverview(
            snapshot.League.Name,
            configuration.RulesetName,
            snapshot.CurrentSeason.Year,
            configuration.RegularSeasonGameCount,
            configuration.RosterLimits.MinimumPlayers,
            configuration.RosterLimits.MaximumPlayers,
            new CapThresholdSummary(
                configuration.PayrollFloor?.SmallestUnits,
                configuration.SoftCap?.SmallestUnits,
                configuration.LuxuryTax?.SmallestUnits,
                configuration.FirstApron?.SmallestUnits,
                configuration.SecondApron?.SmallestUnits,
                configuration.HardCap?.SmallestUnits),
            teamSummaries,
            pickBoardResult.Value,
            BuildFreeAgentMarket(snapshot));

        return DomainOperationResult<LeagueOverview>.Success(overview);
    }

    /// <summary>
    /// Who is unsigned, with what this league permits anyone to pay them. A free agent is a loaded
    /// player on nobody's roster and under no live contract — both checks, because a player released
    /// mid-contract leaves a roster before their dead money leaves the books, and one of those
    /// answers alone would put them in the wrong list.
    /// </summary>
    private FreeAgentMarketSummary BuildFreeAgentMarket(LeagueSnapshot snapshot)
    {
        var rosteredPlayerIds = snapshot.Teams
            .SelectMany(team => team.PlayerIds)
            .ToHashSet();

        // Season-scoped: a contract that simply ran its course is not `IsTerminated` (that flag is
        // only ever set by a release), so once the current season has moved past its last covered
        // season, `TermFor` on it is null and it stops blocking free agency — no different from a
        // released player, just without the dead-money history.
        var contractedPlayerIds = snapshot.Contracts
            .Where(contract => !contract.IsTerminated && contract.TermFor(snapshot.CurrentSeason) is not null)
            .Select(contract => contract.PlayerId)
            .ToHashSet();

        var negotiation = snapshot.Configuration.Negotiation;

        var players = snapshot.Players
            .Where(player => !rosteredPlayerIds.Contains(player.Id) && !contractedPlayerIds.Contains(player.Id))
            .Select(player =>
            {
                var limits = _signingEngine.LimitsFor(snapshot, player.SeasonsOfService);

                return new FreeAgentLine(
                    player.Id.Value,
                    player.FullName,
                    DescribePosition(player.Position),
                    player.Rating.Overall,
                    player.AgeOn(AgeReferenceDate(snapshot.CurrentSeason)),
                    player.SeasonsOfService,
                    player.IsInjured,
                    player.CurrentInjury?.Description,
                    limits.Minimum?.SmallestUnits,
                    limits.Maximum?.SmallestUnits);
            })
            .OrderByDescending(line => line.Overall)
            .ThenBy(line => line.FullName, StringComparer.Ordinal)
            .ToList();

        return new FreeAgentMarketSummary(
            players,
            negotiation.HasCompensationFloor,
            negotiation.HasCompensationCeiling,
            negotiation.MaximumContractSeasons);
    }

    /// <summary>
    /// The date ages are read at. A season is a year until the calendar milestone gives it real
    /// dates, so every age on screen is "as at the first of the season's year" — stated here once
    /// rather than left as an unexplained constant, and the one place to change when a real calendar
    /// arrives. Reading a clock instead would age every player in a save between two launches.
    /// </summary>
    private static DateOnly AgeReferenceDate(Season season) => new(season.Year, 1, 1);

    /// <summary>
    /// Flattens the draft-asset board the rules layer builds, resolving franchise names and hanging
    /// each asset's ledger history off it. The board starts at the <em>next</em> draft: the current
    /// season's draft has already been settled by the time a league is loaded, and a board offering
    /// to trade a pick that has already been used is worse than no board.
    /// </summary>
    private DomainOperationResult<PickBoardSummary> BuildPickBoard(
        LeagueSnapshot snapshot,
        IReadOnlyDictionary<FranchiseId, Franchise> franchisesById)
    {
        var franchises = snapshot.Franchises
            .OrderBy(franchise => franchise.Name, StringComparer.Ordinal)
            .Select(franchise => new FranchiseDraftIdentity(franchise.Id, franchise.Name))
            .ToList();

        var firstDraftSeason = new Season(snapshot.CurrentSeason.Year + 1);
        var boardResult = _draftAssetLedger.BuildBoard(
            snapshot.DraftAssets,
            franchises,
            firstDraftSeason,
            snapshot.Configuration);

        if (boardResult.IsFailure)
        {
            return DomainOperationResult<PickBoardSummary>.Failure(boardResult.Errors.ToArray());
        }

        var board = boardResult.Value;
        var rows = board.Rows
            .Select(row => new FranchisePickRow(
                row.FranchiseId.Value,
                NameOf(row.FranchiseId, franchisesById),
                row.Drafts
                    .Select(cell => new FranchisePickCell(
                        cell.DraftSeason.Year,
                        cell.Assets
                            .Select(asset => ToSummary(asset, snapshot, franchisesById))
                            .ToList()))
                    .ToList()))
            .ToList();

        var seasons = Enumerable
            .Range(board.FirstDraftSeason.Year, board.DraftCount)
            .ToList();

        return DomainOperationResult<PickBoardSummary>.Success(
            new PickBoardSummary(board.FirstDraftSeason.Year, board.DraftCount, board.RoundCount, seasons, rows));
    }

    private static PickAssetSummary ToSummary(
        PickAssetLine asset,
        LeagueSnapshot snapshot,
        IReadOnlyDictionary<FranchiseId, Franchise> franchisesById)
    {
        var originalName = NameOf(asset.OriginalFranchiseId, franchisesById);
        var history = snapshot.Ledger
            .EntriesForPick(asset.PickId)
            .OrderBy(entry => entry.Sequence)
            .Select(ToLine)
            .ToList();

        return new PickAssetSummary(
            asset.PickId.Value,
            asset.Round,
            $"Round {asset.Round} · {originalName}",
            DescribeControlState(asset.State),
            originalName,
            NameOf(asset.CurrentOwnerFranchiseId, franchisesById),
            asset.CounterpartyFranchiseId is null ? null : NameOf(asset.CounterpartyFranchiseId, franchisesById),
            asset.ProtectionSummary,
            asset.OutcomeIfProtectionHolds,
            history);
    }

    private static TransactionLine ToLine(TransactionEntry entry) =>
        new(
            entry.RecordedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            DescribeTransactionKind(entry.Kind),
            entry.Amount?.SmallestUnits,
            entry.Reason);

    private static string NameOf(FranchiseId franchiseId, IReadOnlyDictionary<FranchiseId, Franchise> franchisesById) =>
        franchisesById.TryGetValue(franchiseId, out var franchise)
            ? franchise.Name
            : $"Unknown franchise ({franchiseId.Value})";

    private static string DescribeControlState(PickControlState state) => state switch
    {
        PickControlState.OwnedOutright => "Own",
        PickControlState.OwedAway => "Owed",
        PickControlState.Acquired => "Acquired",
        PickControlState.Incoming => "Owed to you",
        PickControlState.SwapEncumbered => "Swappable",
        PickControlState.SwapRightHeld => "Swap right",
        PickControlState.TradedAway => "Gone",
        _ => state.ToString(),
    };

    private DomainOperationResult<TeamCapSummary> BuildCapSummary(
        LeagueSnapshot snapshot,
        Team team,
        IReadOnlyDictionary<PlayerId, Player> playersById)
    {
        var charges = CapChargeProjection.ForTeamSeason(snapshot.Contracts, team.Id, snapshot.CurrentSeason);
        var capSheetResult = _capLedger.Evaluate(
            team.Id,
            snapshot.CurrentSeason,
            charges,
            team.RosterCount,
            snapshot.Configuration);
        if (capSheetResult.IsFailure)
        {
            return DomainOperationResult<TeamCapSummary>.Failure(capSheetResult.Errors.ToArray());
        }

        var capSheet = capSheetResult.Value;

        var chargeLines = capSheet.Charges
            .Select(charge => new CapChargeLine(
                DescribeChargeSubject(charge, playersById),
                DescribeChargeKind(charge.Kind),
                charge.Amount.SmallestUnits,
                charge.IsDeadMoney))
            .ToList();

        var transactionLines = snapshot.Ledger
            .EntriesForTeam(team.Id)
            .OrderByDescending(entry => entry.Sequence)
            .Take(RecentTransactionCount)
            .Select(ToLine)
            .ToList();

        return DomainOperationResult<TeamCapSummary>.Success(new TeamCapSummary(
            capSheet.Season.Year,
            capSheet.CommittedSalary.SmallestUnits,
            capSheet.DeadMoney.SmallestUnits,
            capSheet.RosterHolds.SmallestUnits,
            capSheet.TotalPayroll.SmallestUnits,
            capSheet.Thresholds.Select(ToSummary).ToList(),
            chargeLines,
            transactionLines));
    }

    private static ThresholdStandingSummary ToSummary(ThresholdStanding standing) =>
        new(
            DescribeThreshold(standing.Kind),
            standing.RuleCode,
            standing.Amount.SmallestUnits,
            standing.SignedDistanceSmallestUnits,
            standing.IsOver,
            standing.IsBreached,
            standing.IsFloor,
            standing.Explanation);

    private static string ResolveFranchiseName(
        Team team,
        IReadOnlyDictionary<FranchiseId, Franchise> franchisesById,
        List<DomainError> errors)
    {
        if (franchisesById.TryGetValue(team.FranchiseId, out var franchise))
        {
            return franchise.Name;
        }

        errors.Add(new DomainError(
            UnknownFranchiseCode,
            $"Team '{team.Name}' references franchise '{team.FranchiseId.Value}', which was not loaded."));
        return string.Empty;
    }

    private static List<RosterSpot> ResolveRoster(
        Team team,
        LeagueSnapshot snapshot,
        IReadOnlyDictionary<PlayerId, Player> playersById,
        List<DomainError> errors)
    {
        var roster = new List<RosterSpot>();
        var contractsByPlayer = snapshot.Contracts
            .Where(contract => contract.TeamId == team.Id && !contract.IsTerminated)
            .ToDictionary(contract => contract.PlayerId);

        foreach (var playerId in team.PlayerIds)
        {
            if (!playersById.TryGetValue(playerId, out var player))
            {
                errors.Add(new DomainError(
                    UnknownPlayerCode,
                    $"Team '{team.Name}' references player '{playerId.Value}', which was not loaded."));
                continue;
            }

            contractsByPlayer.TryGetValue(playerId, out var contract);

            roster.Add(new RosterSpot(
                player.Id.Value,
                player.FullName,
                DescribePosition(player.Position),
                player.Rating.Overall,
                player.IsInjured,
                player.CurrentInjury?.Description,
                contract?.ChargeFor(snapshot.CurrentSeason)?.Amount.SmallestUnits ?? 0,
                SeasonsRemaining(contract, snapshot.CurrentSeason)));
        }

        // Best player first: the roster grid's whole job at this milestone is "who have I got".
        return roster
            .OrderByDescending(spot => spot.Overall)
            .ThenBy(spot => spot.FullName, StringComparer.Ordinal)
            .ToList();
    }

    private static int SeasonsRemaining(Contract? contract, Season currentSeason) =>
        contract is null
            ? 0
            : contract.Terms.Count(term => term.Season.Year >= currentSeason.Year && !term.IsDeclinedOption);

    /// <summary>
    /// What the charge is <em>for</em>. Usually a player; a roster-slot hold has none, because it is
    /// a charge for a signing that has not happened yet, and naming it as an unknown player would
    /// invent a person.
    /// </summary>
    private static string DescribeChargeSubject(CapCharge charge, IReadOnlyDictionary<PlayerId, Player> playersById)
    {
        if (charge.PlayerId is not { } playerId)
        {
            return "Unfilled roster spot";
        }

        return playersById.TryGetValue(playerId, out var player)
            ? player.FullName
            // A charge whose player was not loaded is still money on the books: showing the raw
            // identifier keeps the total honest instead of hiding a line the payroll includes.
            : $"Unknown player ({playerId.Value})";
    }

    private static string DescribeChargeKind(CapChargeKind kind) => kind switch
    {
        CapChargeKind.ActiveContract => "Active contract",
        CapChargeKind.DeadMoney => "Dead money",
        CapChargeKind.RosterSlotHold => "Roster-slot hold",
        _ => kind.ToString(),
    };

    private static string DescribeTransactionKind(TransactionKind kind) => kind switch
    {
        TransactionKind.ContractSigned => "Contract signed",
        TransactionKind.PlayerReleased => "Player released",
        TransactionKind.OptionExercised => "Option exercised",
        TransactionKind.OptionDeclined => "Option declined",
        TransactionKind.DraftPickTransferred => "Pick traded",
        TransactionKind.DraftPickEncumbered => "Pick encumbered",
        TransactionKind.DraftPickConveyed => "Pick conveyed",
        TransactionKind.DraftPickRolledOver => "Protection held",
        TransactionKind.DraftPickConverted => "Obligation converted",
        TransactionKind.DraftPickExtinguished => "Obligation extinguished",
        TransactionKind.SwapRightExercised => "Swap exercised",
        TransactionKind.SwapRightDeclined => "Swap declined",
        TransactionKind.PlayerTraded => "Player traded",
        _ => kind.ToString(),
    };

    private static string DescribeThreshold(CapThresholdKind kind) => kind switch
    {
        CapThresholdKind.SoftCap => "Soft cap",
        CapThresholdKind.LuxuryTax => "Luxury tax",
        CapThresholdKind.FirstApron => "First apron",
        CapThresholdKind.SecondApron => "Second apron",
        CapThresholdKind.HardCap => "Hard cap",
        CapThresholdKind.PayrollFloor => "Payroll floor",
        _ => kind.ToString(),
    };

    /// <summary>
    /// The short label every screen shows a position by. Internal rather than private because the
    /// free-agency board columns by position too, and two copies of this map is two places for a
    /// league's positions to stop agreeing with each other.
    /// </summary>
    internal static string DescribePosition(Position position) => position switch
    {
        Position.PointGuard => "PG",
        Position.ShootingGuard => "SG",
        Position.SmallForward => "SF",
        Position.PowerForward => "PF",
        Position.Center => "C",
        _ => position.ToString(),
    };
}
