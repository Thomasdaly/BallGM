using BallGM.Application.Cap;
using BallGM.Application.DraftAssets;
using BallGM.Application.Trades;
using BallGM.Domain.Cap;
using BallGM.Domain.Common;
using BallGM.Domain.DraftAssets;
using BallGM.Domain.Leagues;
using BallGM.Domain.Players;
using BallGM.Domain.Teams;
using BallGM.Domain.Trades;

namespace BallGM.Application.Leagues;

/// <summary>
/// One league, held in memory for as long as it is being played.
/// <para>
/// Every screen before this milestone could reload the league from its data source on demand,
/// because nothing changed it. A trade does change it, so something has to own the loaded league
/// between commands — reloading after an execution would discard the very change the screen exists
/// to show. This is that owner, and it is the natural home for advancing the calendar later.
/// </para>
/// <para>
/// Saving is still out of scope: the session holds a league for the length of a run, and closing the
/// client discards it. Persistence arrives with save migrations.
/// </para>
/// </summary>
public sealed class LeagueSession
{
    private const string NotLoadedCode = "league_session.not_loaded";
    private const string UnknownTeamCode = "trade_request.unknown_team";
    private const string UnknownAssetCode = "trade_request.unknown_asset";
    private const string UnknownAssetKindCode = "trade_request.unknown_asset_kind";

    private readonly ILeagueDataSource _dataSource;
    private readonly ITradeEngine _tradeEngine;
    private readonly GetLeagueOverviewQuery _overviewQuery;

    private LeagueSnapshot? _snapshot;

    public LeagueSession(
        ILeagueDataSource dataSource,
        ICapLedger capLedger,
        IDraftAssetLedger draftAssetLedger,
        ITradeEngine tradeEngine)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(capLedger);
        ArgumentNullException.ThrowIfNull(draftAssetLedger);
        ArgumentNullException.ThrowIfNull(tradeEngine);

        _dataSource = dataSource;
        _tradeEngine = tradeEngine;
        _overviewQuery = new GetLeagueOverviewQuery(dataSource, capLedger, draftAssetLedger);
    }

    public bool IsLoaded => _snapshot is not null;

    /// <summary>Loads the league once. Calling it again reloads, discarding anything done since.</summary>
    public DomainOperationResult<LeagueOverview> Load()
    {
        var snapshotResult = _dataSource.Load();
        if (snapshotResult.IsFailure)
        {
            return DomainOperationResult<LeagueOverview>.Failure(snapshotResult.Errors.ToArray());
        }

        _snapshot = snapshotResult.Value;
        return _overviewQuery.Project(_snapshot);
    }

    /// <summary>The current state of the held league, re-projected from the aggregates as they stand.</summary>
    public DomainOperationResult<LeagueOverview> Overview() =>
        _snapshot is null
            ? NotLoaded<LeagueOverview>()
            : _overviewQuery.Project(_snapshot);

    /// <summary>
    /// Judges a proposal without changing anything. Safe to call on every keystroke: the trade
    /// machine's whole job is to answer "what if" over and over.
    /// </summary>
    public DomainOperationResult<TradeAssessmentSummary> AssessTrade(TradeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_snapshot is null)
        {
            return NotLoaded<TradeAssessmentSummary>();
        }

        var proposalResult = BuildProposal(request, _snapshot);
        if (proposalResult.IsFailure)
        {
            return DomainOperationResult<TradeAssessmentSummary>.Failure(proposalResult.Errors.ToArray());
        }

        var assessmentResult = _tradeEngine.Assess(proposalResult.Value, _snapshot);
        return assessmentResult.IsFailure
            ? DomainOperationResult<TradeAssessmentSummary>.Failure(assessmentResult.Errors.ToArray())
            : DomainOperationResult<TradeAssessmentSummary>.Success(ToSummary(assessmentResult.Value, _snapshot));
    }

    /// <summary>
    /// Executes a proposal against the league as it stands right now. The proposal is rebuilt from
    /// the request at this moment, so the state token it carries is current — and the engine still
    /// re-validates, because between assessing and submitting, anything could have happened.
    /// </summary>
    public DomainOperationResult<TradeSubmission> SubmitTrade(TradeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_snapshot is null)
        {
            return NotLoaded<TradeSubmission>();
        }

        var proposalResult = BuildProposal(request, _snapshot);
        if (proposalResult.IsFailure)
        {
            return DomainOperationResult<TradeSubmission>.Failure(proposalResult.Errors.ToArray());
        }

        var executionResult = _tradeEngine.Execute(proposalResult.Value, _snapshot);
        if (executionResult.IsFailure)
        {
            return DomainOperationResult<TradeSubmission>.Failure(executionResult.Errors.ToArray());
        }

        var overviewResult = _overviewQuery.Project(_snapshot);
        if (overviewResult.IsFailure)
        {
            return DomainOperationResult<TradeSubmission>.Failure(overviewResult.Errors.ToArray());
        }

        return DomainOperationResult<TradeSubmission>.Success(new TradeSubmission(
            ToSummary(executionResult.Value.Assessment, _snapshot),
            executionResult.Value.LedgerEntryCount,
            overviewResult.Value));
    }

    /// <summary>
    /// Turns identifiers from a read model into a domain proposal, failing explainably on anything
    /// that no longer exists — a screen can legitimately hold an identifier the league has moved on
    /// from, and that is a message, not a crash.
    /// </summary>
    private static DomainOperationResult<TradeProposal> BuildProposal(TradeRequest request, LeagueSnapshot snapshot)
    {
        var errors = new List<DomainError>();
        var teamIds = new List<TeamId>();

        foreach (var teamId in request.TeamIds)
        {
            var team = snapshot.Teams.FirstOrDefault(candidate => candidate.Id.Value == teamId);
            if (team is null)
            {
                errors.Add(new DomainError(UnknownTeamCode, $"Team '{teamId}' is not a team in this league."));
                continue;
            }

            teamIds.Add(team.Id);
        }

        var movements = new List<TradeAssetMovement>();

        foreach (var asset in request.Assets)
        {
            var from = snapshot.Teams.FirstOrDefault(team => team.Id.Value == asset.FromTeamId);
            var to = snapshot.Teams.FirstOrDefault(team => team.Id.Value == asset.ToTeamId);

            if (from is null || to is null)
            {
                errors.Add(new DomainError(
                    UnknownTeamCode,
                    $"Asset '{asset.AssetId}' moves between teams that are not both in this league."));
                continue;
            }

            switch (asset.AssetKind)
            {
                case TradeAssetRequest.PlayerKind:
                    var player = snapshot.Players.FirstOrDefault(candidate => candidate.Id.Value == asset.AssetId);
                    if (player is null)
                    {
                        errors.Add(new DomainError(UnknownAssetCode, $"Player '{asset.AssetId}' is not in this league."));
                        break;
                    }

                    movements.Add(TradeAssetMovement.Player(player.Id, from.Id, to.Id));
                    break;

                case TradeAssetRequest.PickKind:
                    var pick = snapshot.DraftAssets.Pick(new DraftPickId(asset.AssetId));
                    if (pick is null)
                    {
                        errors.Add(new DomainError(UnknownAssetCode, $"Pick '{asset.AssetId}' is not a draft asset in this league."));
                        break;
                    }

                    movements.Add(TradeAssetMovement.DraftPick(pick.Id, from.Id, to.Id));
                    break;

                default:
                    errors.Add(new DomainError(
                        UnknownAssetKindCode,
                        $"'{asset.AssetKind}' is not an asset kind a trade can move. Expected '{TradeAssetRequest.PlayerKind}' or '{TradeAssetRequest.PickKind}'."));
                    break;
            }
        }

        if (errors.Count > 0)
        {
            return DomainOperationResult<TradeProposal>.Failure(errors.ToArray());
        }

        return TradeProposal.Create(
            new TradeId(SortableId.NewId()),
            snapshot.CurrentSeason,
            teamIds,
            movements,
            LeagueStateToken.From(snapshot.Ledger));
    }

    private static TradeAssessmentSummary ToSummary(TradeAssessment assessment, LeagueSnapshot snapshot)
    {
        var teamNames = snapshot.Teams.ToDictionary(team => team.Id, team => team.Name);

        return new TradeAssessmentSummary(
            assessment.IsLegal,
            assessment.Violations.Select(finding => ToLine(finding, teamNames)).ToList(),
            assessment.Warnings.Select(finding => ToLine(finding, teamNames)).ToList(),
            assessment.Outcomes.Select(outcome => ToLine(outcome, teamNames)).ToList());
    }

    private static TradeFindingLine ToLine(TradeRuleFinding finding, IReadOnlyDictionary<TeamId, string> teamNames) =>
        new(
            finding.RuleCode,
            finding.Explanation,
            finding.TeamId is null ? null : teamNames.GetValueOrDefault(finding.TeamId, finding.TeamId.Value));

    private static TradeTeamOutcomeLine ToLine(TradeTeamOutcome outcome, IReadOnlyDictionary<TeamId, string> teamNames) =>
        new(
            outcome.TeamId.Value,
            teamNames.GetValueOrDefault(outcome.TeamId, outcome.TeamId.Value),
            outcome.IncomingSalary.SmallestUnits,
            outcome.OutgoingSalary.SmallestUnits,
            outcome.PayrollBefore.SmallestUnits,
            outcome.PayrollAfter.SmallestUnits,
            outcome.PayrollChangeSmallestUnits,
            outcome.RosterCountBefore,
            outcome.RosterCountAfter,
            outcome.PicksBefore,
            outcome.PicksAfter,
            outcome.ThresholdsAfter.Select(ToSummary).ToList());

    private static ThresholdStandingSummary ToSummary(ThresholdStanding standing) =>
        new(
            standing.Kind.ToString(),
            standing.RuleCode,
            standing.Amount.SmallestUnits,
            standing.SignedDistanceSmallestUnits,
            standing.IsOver,
            standing.Explanation);

    private static DomainOperationResult<T> NotLoaded<T>() =>
        DomainOperationResult<T>.Failure(new DomainError(
            NotLoadedCode,
            "No league is loaded in this session. Load one before reading it or trading in it."));
}
