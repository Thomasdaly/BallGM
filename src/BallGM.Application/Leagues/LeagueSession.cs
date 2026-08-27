using BallGM.Application.Cap;
using BallGM.Application.DraftAssets;
using BallGM.Application.Negotiations;
using BallGM.Application.Trades;
using BallGM.Domain.Cap;
using BallGM.Domain.Common;
using BallGM.Domain.Contracts;
using BallGM.Domain.DraftAssets;
using BallGM.Domain.Leagues;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Players;
using BallGM.Domain.Randomness;
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
public sealed partial class LeagueSession
{
    private const string NotLoadedCode = "league_session.not_loaded";
    private const string UnknownTeamCode = "trade_request.unknown_team";
    private const string UnknownAssetCode = "trade_request.unknown_asset";
    private const string UnknownAssetKindCode = "trade_request.unknown_asset_kind";
    private const string EmptyOfferCode = "offer_request.no_seasons";

    /// <summary>
    /// The seed every free-agency tie-break is drawn from until a save carries one. A constant rather
    /// than a clock: two runs of the same league from the same fixture have to resolve the same
    /// market the same way, or nothing about free agency is reproducible.
    /// </summary>
    public const int DefaultMarketSeed = 20260828;

    private readonly ILeagueDataSource _dataSource;
    private readonly ITradeEngine _tradeEngine;
    private readonly ISigningEngine _signingEngine;
    private readonly IFreeAgencyMarket _freeAgencyMarket;
    private readonly IRandomSource _marketRandom;
    private readonly GetLeagueOverviewQuery _overviewQuery;

    /// <summary>
    /// The negotiations currently running, keyed by the player whose market each one is. Held here
    /// rather than on <see cref="LeagueSnapshot"/> because an in-flight negotiation is market state
    /// this session owns for as long as free agency is running, not league state every screen has to
    /// project. It survives a signing, a trade, and a re-projection, and it is what a save writes.
    /// </summary>
    private readonly Dictionary<string, Negotiation> _negotiations = [];

    private LeagueSnapshot? _snapshot;

    public LeagueSession(
        ILeagueDataSource dataSource,
        ICapLedger capLedger,
        IDraftAssetLedger draftAssetLedger,
        ITradeEngine tradeEngine,
        ISigningEngine signingEngine,
        IFreeAgencyMarket freeAgencyMarket,
        int marketSeed = DefaultMarketSeed)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(capLedger);
        ArgumentNullException.ThrowIfNull(draftAssetLedger);
        ArgumentNullException.ThrowIfNull(tradeEngine);
        ArgumentNullException.ThrowIfNull(signingEngine);
        ArgumentNullException.ThrowIfNull(freeAgencyMarket);

        _dataSource = dataSource;
        _tradeEngine = tradeEngine;
        _signingEngine = signingEngine;
        _freeAgencyMarket = freeAgencyMarket;
        _marketRandom = new SeededRandomSource(marketSeed);
        _overviewQuery = new GetLeagueOverviewQuery(dataSource, capLedger, draftAssetLedger, signingEngine);
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
    /// Judges an offer without changing anything. The offer screen's counterpart to
    /// <see cref="AssessTrade"/>, and safe to call on every keystroke for the same reason.
    /// </summary>
    public DomainOperationResult<SigningAssessmentSummary> AssessOffer(OfferRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_snapshot is null)
        {
            return NotLoaded<SigningAssessmentSummary>();
        }

        var partiesResult = ResolveParties(request, _snapshot);
        if (partiesResult.IsFailure)
        {
            return DomainOperationResult<SigningAssessmentSummary>.Failure(partiesResult.Errors.ToArray());
        }

        var (team, player) = partiesResult.Value;

        var offerResult = BuildOffer(request, team, player);
        if (offerResult.IsFailure)
        {
            return DomainOperationResult<SigningAssessmentSummary>.Failure(offerResult.Errors.ToArray());
        }

        var assessmentResult = _signingEngine.Assess(offerResult.Value, _snapshot, team.Id, player.Id);
        return assessmentResult.IsFailure
            ? DomainOperationResult<SigningAssessmentSummary>.Failure(assessmentResult.Errors.ToArray())
            : DomainOperationResult<SigningAssessmentSummary>.Success(ToSummary(assessmentResult.Value, team.Name, player.FullName));
    }

    /// <summary>
    /// Signs the player, against the league as it stands right now. The offer is rebuilt from the
    /// request at this moment and the engine re-validates regardless, because between assessing and
    /// submitting, another team could have signed the same player.
    /// <para>
    /// A signing is the one transaction so far that <em>creates</em> an aggregate rather than moving
    /// one that already exists, so the session replaces the league it holds with one that includes
    /// the new contract. Every other consumer keeps reading it through the same projection.
    /// </para>
    /// </summary>
    public DomainOperationResult<SigningSubmission> SubmitOffer(OfferRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_snapshot is null)
        {
            return NotLoaded<SigningSubmission>();
        }

        var partiesResult = ResolveParties(request, _snapshot);
        if (partiesResult.IsFailure)
        {
            return DomainOperationResult<SigningSubmission>.Failure(partiesResult.Errors.ToArray());
        }

        var (team, player) = partiesResult.Value;

        var offerResult = BuildOffer(request, team, player);
        if (offerResult.IsFailure)
        {
            return DomainOperationResult<SigningSubmission>.Failure(offerResult.Errors.ToArray());
        }

        var executionResult = _signingEngine.Execute(offerResult.Value, _snapshot, team.Id, player.Id);
        if (executionResult.IsFailure)
        {
            return DomainOperationResult<SigningSubmission>.Failure(executionResult.Errors.ToArray());
        }

        var execution = executionResult.Value;
        _snapshot = _snapshot with { Contracts = [.. _snapshot.Contracts, execution.Contract] };

        var overviewResult = _overviewQuery.Project(_snapshot);
        if (overviewResult.IsFailure)
        {
            return DomainOperationResult<SigningSubmission>.Failure(overviewResult.Errors.ToArray());
        }

        return DomainOperationResult<SigningSubmission>.Success(new SigningSubmission(
            ToSummary(execution.Assessment, team.Name, player.FullName),
            DescribeRoute(execution.Route),
            execution.LedgerEntryCount,
            overviewResult.Value));
    }

    /// <summary>
    /// Resolves the two parties an offer names. A screen can legitimately hold an identifier the
    /// league has moved on from, and that is a message rather than a crash.
    /// </summary>
    private static DomainOperationResult<(Team Team, Player Player)> ResolveParties(
        OfferRequest request,
        LeagueSnapshot snapshot)
    {
        var errors = new List<DomainError>();

        var team = snapshot.Teams.FirstOrDefault(candidate => candidate.Id.Value == request.TeamId);
        if (team is null)
        {
            errors.Add(new DomainError(UnknownTeamCode, $"Team '{request.TeamId}' is not a team in this league."));
        }

        var player = snapshot.Players.FirstOrDefault(candidate => candidate.Id.Value == request.PlayerId);
        if (player is null)
        {
            errors.Add(new DomainError(UnknownAssetCode, $"Player '{request.PlayerId}' is not in this league."));
        }

        return errors.Count > 0
            ? DomainOperationResult<(Team, Player)>.Failure(errors.ToArray())
            : DomainOperationResult<(Team, Player)>.Success((team!, player!));
    }

    private static DomainOperationResult<Offer> BuildOffer(OfferRequest request, Team team, Player player)
    {
        if (request.Seasons.Count == 0)
        {
            return DomainOperationResult<Offer>.Failure(new DomainError(
                EmptyOfferCode,
                "An offer has to cover at least one season."));
        }

        var terms = request.Seasons
            .Select(season => new ContractSeasonTerm(
                new Season(season.SeasonYear),
                new Money(season.Compensation),
                new Money(season.GuaranteedAmount)))
            .ToList();

        return Offer.Create(new OfferId(SortableId.NewId()), team.Id, player.Id, terms);
    }

    private static SigningAssessmentSummary ToSummary(SigningAssessment assessment, string teamName, string playerName) =>
        new(
            assessment.IsLegal,
            assessment.Offer.PlayerId.Value,
            playerName,
            assessment.Offer.TeamId.Value,
            teamName,
            assessment.Offer.SeasonCount,
            assessment.Offer.FirstSeasonCompensation.SmallestUnits,
            assessment.Offer.TotalCompensation.SmallestUnits,
            assessment.Offer.TotalGuaranteed.SmallestUnits,
            assessment.Violations.Select(ToSigningLine).ToList(),
            assessment.Warnings.Select(ToSigningLine).ToList(),
            assessment.Notes.Select(ToSigningLine).ToList(),
            assessment.Routes.Select(ToLine).ToList(),
            assessment.PermittingRoute is null ? null : DescribeRoute(assessment.PermittingRoute.Kind),
            assessment.PayrollBefore.SmallestUnits,
            assessment.PayrollAfter.SmallestUnits,
            assessment.RosterCountBefore,
            assessment.RosterCountAfter,
            assessment.CapRoomBefore?.SmallestUnits);

    private static SigningFindingLine ToSigningLine(RuleFinding finding) =>
        new(finding.RuleCode, finding.Explanation);

    private static SigningRouteLine ToLine(SigningRouteEvaluation route) =>
        new(
            DescribeRoute(route.Kind),
            route.Applicable,
            route.Permits,
            route.MaximumFirstSeasonCompensation?.SmallestUnits,
            route.RuleCode,
            route.Explanation);

    private static string DescribeRoute(SigningRouteKind kind) => kind switch
    {
        SigningRouteKind.UnrestrictedSigning => "Unrestricted signing",
        SigningRouteKind.CapRoom => "Cap room",
        SigningRouteKind.MinimumSalary => "Minimum salary",
        SigningRouteKind.StandardOverCapAllowance => "Standard over-cap allowance",
        _ => kind.ToString(),
    };

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
            assessment.Notes.Select(finding => ToLine(finding, teamNames)).ToList(),
            assessment.Outcomes.Select(outcome => ToLine(outcome, teamNames)).ToList());
    }

    private static TradeFindingLine ToLine(RuleFinding finding, IReadOnlyDictionary<TeamId, string> teamNames) =>
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
            standing.IsBreached,
            standing.IsFloor,
            standing.Explanation);

    private static DomainOperationResult<T> NotLoaded<T>() =>
        DomainOperationResult<T>.Failure(new DomainError(
            NotLoadedCode,
            "No league is loaded in this session. Load one before reading it, trading in it, or signing anyone."));
}
