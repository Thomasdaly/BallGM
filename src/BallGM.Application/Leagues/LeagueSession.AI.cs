using BallGM.Application.AI;
using BallGM.Domain.AI;
using BallGM.Domain.Common;
using BallGM.Domain.Players;
using BallGM.Domain.Seasons;
using BallGM.Domain.Teams;
using BallGM.Domain.Trades;

namespace BallGM.Application.Leagues;

/// <summary>
/// The Milestone 9 half of the session: what a team's own front office would read
/// (<see cref="FrontOfficeAdvisory"/>) and, since the AI-turn-execution slice, what it actually does
/// with a candidate it found (<see cref="RunAiFrontOfficeTurn"/>).
/// <para>
/// <see cref="RunAiFrontOfficeTurn"/> is deliberately not a new execution path: it calls exactly the
/// same <see cref="IFrontOfficeAdvisor.FindTradeTargets"/>/<see cref="IFrontOfficeAdvisor.FindFreeAgentTargets"/>
/// "assess" halves <see cref="FrontOfficeAdvisory"/> already calls, then hands the first candidate to
/// the same <see cref="Trades.ITradeEngine.Execute"/>/<see cref="Negotiations.ISigningEngine.Execute"/>
/// "execute" halves <see cref="SubmitTrade"/>/<see cref="SubmitOffer"/> already call. Nothing new
/// validates, moves a roster, or touches the cap sheet; this is glue, not a fifth engine.
/// </para>
/// <para>
/// It also takes no view on which teams are "AI-controlled" — that is still an open question (see
/// <c>docs/architecture.md</c> → "AI turn execution: acting on a candidate"), and there is no
/// persisted flag on <see cref="Domain.Teams.Team"/> to answer it with. The caller states which team
/// IDs to run a turn for, every single time; this method is what a season-boundary loop or a client
/// "run AI turn" action would eventually call, not something that runs itself.
/// </para>
/// </summary>
public sealed partial class LeagueSession
{
    private const string AIUnknownTeamCode = "ai_advisory.unknown_team";
    private const string AiTurnNoCandidateCode = "ai_turn.no_candidate";
    private const string AiTurnTradeExecutionFailedCode = "ai_turn.trade_execution_failed";
    private const string AiTurnSigningExecutionFailedCode = "ai_turn.signing_execution_failed";

    /// <summary>
    /// The seed the draft-decision preview is generated from. A constant rather than a clock, for the
    /// same reproducibility reason every other seed on this type is — a preview a GM cannot re-derive
    /// is not an explainable one, even though it is never the class the real draft will actually use.
    /// </summary>
    public const int DefaultDraftPreviewSeed = 20260901;

    /// <summary>
    /// Everything this team's front office would read and do right now: its classified direction and
    /// positional needs, the trades and free-agent offers it could legally pursue today, and a preview
    /// of what it would take with a draft selection. Safe to call as often as a screen likes — nothing
    /// here changes the league.
    /// </summary>
    public DomainOperationResult<FrontOfficeAdvisorySummary> FrontOfficeAdvisory(string teamId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamId);

        if (_snapshot is null)
        {
            return NotLoaded<FrontOfficeAdvisorySummary>();
        }

        var team = _snapshot.Teams.FirstOrDefault(candidate => candidate.Id.Value == teamId);
        if (team is null)
        {
            return DomainOperationResult<FrontOfficeAdvisorySummary>.Failure(new DomainError(
                AIUnknownTeamCode, $"Team '{teamId}' is not a team in this league."));
        }

        var standing = _seasonRun is null ? null : _seasonEngine.Standings(_seasonRun, _snapshot).Row(team.Id);

        var assessmentResult = _frontOfficeAdvisor.AssessFrontOffice(team.Id, _snapshot, standing);
        if (assessmentResult.IsFailure)
        {
            return DomainOperationResult<FrontOfficeAdvisorySummary>.Failure(assessmentResult.Errors.ToArray());
        }

        var tradeTargetsResult = _frontOfficeAdvisor.FindTradeTargets(team.Id, _snapshot);
        if (tradeTargetsResult.IsFailure)
        {
            return DomainOperationResult<FrontOfficeAdvisorySummary>.Failure(tradeTargetsResult.Errors.ToArray());
        }

        var freeAgents = _snapshot.Players.Where(player => IsFreeAgent(player, _snapshot)).ToList();
        var freeAgentTargetsResult = _frontOfficeAdvisor.FindFreeAgentTargets(team.Id, freeAgents, _snapshot);
        if (freeAgentTargetsResult.IsFailure)
        {
            return DomainOperationResult<FrontOfficeAdvisorySummary>.Failure(freeAgentTargetsResult.Errors.ToArray());
        }

        var draftResult = _frontOfficeAdvisor.PreviewDraftRecommendation(team.Id, _snapshot, DefaultDraftPreviewSeed);
        if (draftResult.IsFailure)
        {
            return DomainOperationResult<FrontOfficeAdvisorySummary>.Failure(draftResult.Errors.ToArray());
        }

        var playersById = _snapshot.Players.ToDictionary(player => player.Id);
        var teamNames = TeamNames(_snapshot);

        var direction = new OrganisationalDirectionLine(
            assessmentResult.Value.Direction.Direction.ToString(),
            assessmentResult.Value.Direction.Factors.Select(finding => ToAILine(finding, teamNames)).ToList());

        var needs = new RosterNeedsLine(
            assessmentResult.Value.Needs.PositionalNeeds
                .Select(need => new PositionalNeedLine(
                    GetLeagueOverviewQuery.DescribePosition(need.Position),
                    need.Severity.ToString(),
                    need.RuleCode,
                    need.Explanation))
                .ToList(),
            assessmentResult.Value.Needs.Notes.Select(finding => ToAILine(finding, teamNames)).ToList());

        var tradeTargets = tradeTargetsResult.Value
            .Select(candidate => ToLine(candidate, playersById, teamNames))
            .ToList();

        var freeAgentTargets = freeAgentTargetsResult.Value
            .Select(candidate => ToLine(candidate, playersById, teamNames, team.Name))
            .ToList();

        DraftRecommendationLine? draftPreview = null;
        string? draftPreviewNote = null;

        if (draftResult.Value is { } preview)
        {
            draftPreview = new DraftRecommendationLine(
                preview.Prospect.Id.Value,
                preview.Prospect.FullName,
                GetLeagueOverviewQuery.DescribePosition(preview.Prospect.Position),
                preview.Recommendation.Rationale.Select(finding => ToAILine(finding, teamNames)).ToList());
        }
        else if (!_snapshot.Configuration.HasDraft || !_snapshot.Configuration.GeneratesDraftClasses)
        {
            draftPreviewNote = "This league holds no draft, or does not procedurally generate its own classes, so there is no draft preview.";
        }
        else
        {
            draftPreviewNote = "The generated preview class came up empty.";
        }

        return DomainOperationResult<FrontOfficeAdvisorySummary>.Success(new FrontOfficeAdvisorySummary(
            team.Id.Value,
            team.Name,
            direction,
            needs,
            tradeTargets,
            freeAgentTargets,
            draftPreview,
            draftPreviewNote));
    }

    /// <summary>
    /// Runs one AI front-office turn for each named team, in the order given: its first legal trade
    /// target (<see cref="IFrontOfficeAdvisor.FindTradeTargets"/>) is executed if one exists, else its
    /// first legal free-agent target (<see cref="IFrontOfficeAdvisor.FindFreeAgentTargets"/>), else
    /// nothing — see the type's own remarks for why "first" rather than a ranking, and why a trade is
    /// tried before a signing (an arbitrary but stated priority, not a claim that trades matter more).
    /// <para>
    /// Each team's turn sees whatever the previous team in the same call just did — a trade or a
    /// signing mutates the league the same way <see cref="SubmitTrade"/>/<see cref="SubmitOffer"/>
    /// already do, so the second team in a list genuinely cannot take a player the first team just
    /// acquired. There is no other concurrency concern to guard against: this session is
    /// single-threaded, and every execution re-validates against the league as it stands the same way
    /// a human's own submission does, so a candidate that went stale between being found and being
    /// executed fails cleanly (recorded in <see cref="AiTeamTurnOutcome.Notes"/>) rather than
    /// corrupting anything.
    /// </para>
    /// </summary>
    public DomainOperationResult<AiFrontOfficeTurnSummary> RunAiFrontOfficeTurn(IReadOnlyCollection<string> teamIds)
    {
        ArgumentNullException.ThrowIfNull(teamIds);

        if (_snapshot is null)
        {
            return NotLoaded<AiFrontOfficeTurnSummary>();
        }

        var outcomes = new List<AiTeamTurnOutcome>();

        foreach (var teamId in teamIds)
        {
            var team = _snapshot.Teams.FirstOrDefault(candidate => candidate.Id.Value == teamId);
            if (team is null)
            {
                return DomainOperationResult<AiFrontOfficeTurnSummary>.Failure(new DomainError(
                    AIUnknownTeamCode, $"Team '{teamId}' is not a team in this league."));
            }

            var outcomeResult = RunOneAiTurn(team.Id, team.Name);
            if (outcomeResult.IsFailure)
            {
                return DomainOperationResult<AiFrontOfficeTurnSummary>.Failure(outcomeResult.Errors.ToArray());
            }

            outcomes.Add(outcomeResult.Value);
        }

        return DomainOperationResult<AiFrontOfficeTurnSummary>.Success(new AiFrontOfficeTurnSummary(outcomes));
    }

    private DomainOperationResult<AiTeamTurnOutcome> RunOneAiTurn(TeamId teamId, string teamName)
    {
        var playersById = _snapshot!.Players.ToDictionary(player => player.Id);
        var teamNames = TeamNames(_snapshot);
        var notes = new List<AIFindingLine>();

        var tradeTargetsResult = _frontOfficeAdvisor.FindTradeTargets(teamId, _snapshot);
        if (tradeTargetsResult.IsFailure)
        {
            return DomainOperationResult<AiTeamTurnOutcome>.Failure(tradeTargetsResult.Errors.ToArray());
        }

        var trade = tradeTargetsResult.Value.FirstOrDefault();
        if (trade is not null)
        {
            var executionResult = _tradeEngine.Execute(trade.Proposal, _snapshot);
            if (executionResult.IsSuccess)
            {
                return DomainOperationResult<AiTeamTurnOutcome>.Success(new AiTeamTurnOutcome(
                    teamId.Value, teamName, AiTurnAction.TradeExecuted, ToLine(trade, playersById, teamNames), null, []));
            }

            notes.Add(new AIFindingLine(
                AiTurnTradeExecutionFailedCode,
                $"The best available trade could not be executed after all: {string.Join("; ", executionResult.Errors.Select(error => error.Message))}",
                null));
        }

        var freeAgents = _snapshot.Players.Where(player => IsFreeAgent(player, _snapshot)).ToList();
        var freeAgentTargetsResult = _frontOfficeAdvisor.FindFreeAgentTargets(teamId, freeAgents, _snapshot);
        if (freeAgentTargetsResult.IsFailure)
        {
            return DomainOperationResult<AiTeamTurnOutcome>.Failure(freeAgentTargetsResult.Errors.ToArray());
        }

        var offer = freeAgentTargetsResult.Value.FirstOrDefault();
        if (offer is not null)
        {
            var executionResult = _signingEngine.Execute(offer.Offer, _snapshot, teamId, offer.Offer.PlayerId, _seasonRun?.CurrentDay);
            if (executionResult.IsSuccess)
            {
                _snapshot = _snapshot with { Contracts = [.. _snapshot.Contracts, executionResult.Value.Contract] };
                return DomainOperationResult<AiTeamTurnOutcome>.Success(new AiTeamTurnOutcome(
                    teamId.Value, teamName, AiTurnAction.SigningExecuted, null, ToLine(offer, playersById, teamNames, teamName), []));
            }

            notes.Add(new AIFindingLine(
                AiTurnSigningExecutionFailedCode,
                $"The best available free-agent offer could not be executed after all: {string.Join("; ", executionResult.Errors.Select(error => error.Message))}",
                null));
        }

        if (notes.Count == 0)
        {
            notes.Add(new AIFindingLine(
                AiTurnNoCandidateCode,
                "No legal trade or free-agent candidate was found for this team this turn.",
                null));
        }

        return DomainOperationResult<AiTeamTurnOutcome>.Success(new AiTeamTurnOutcome(
            teamId.Value, teamName, AiTurnAction.NoActionTaken, null, null, notes));
    }

    private static AIFindingLine ToAILine(RuleFinding finding, IReadOnlyDictionary<TeamId, string> teamNames) =>
        new(finding.RuleCode, finding.Explanation, finding.TeamId is null ? null : teamNames.GetValueOrDefault(finding.TeamId, finding.TeamId.Value));

    private TradeTargetLine ToLine(
        TradeTargetCandidate candidate,
        IReadOnlyDictionary<PlayerId, Player> playersById,
        IReadOnlyDictionary<TeamId, string> teamNames)
    {
        var incoming = candidate.Proposal.ReceivedBy(candidate.ShoppingTeamId).First(movement => movement.Kind == TradeAssetKind.Player);
        var outgoing = candidate.Proposal.SentBy(candidate.ShoppingTeamId).First(movement => movement.Kind == TradeAssetKind.Player);

        return new TradeTargetLine(
            candidate.CounterpartyTeamId.Value,
            teamNames.GetValueOrDefault(candidate.CounterpartyTeamId, candidate.CounterpartyTeamId.Value),
            incoming.PlayerId!.Value,
            playersById.TryGetValue(incoming.PlayerId!, out var incomingPlayer) ? incomingPlayer.FullName : incoming.PlayerId!.Value,
            outgoing.PlayerId!.Value,
            playersById.TryGetValue(outgoing.PlayerId!, out var outgoingPlayer) ? outgoingPlayer.FullName : outgoing.PlayerId!.Value,
            candidate.Rationale.Select(finding => ToAILine(finding, teamNames)).ToList(),
            ToSummary(candidate.Assessment, _snapshot!));
    }

    private static FreeAgentTargetLine ToLine(
        FreeAgentTargetCandidate candidate,
        IReadOnlyDictionary<PlayerId, Player> playersById,
        IReadOnlyDictionary<TeamId, string> teamNames,
        string shoppingTeamName)
    {
        var playerName = playersById.TryGetValue(candidate.Offer.PlayerId, out var player) ? player.FullName : candidate.Offer.PlayerId.Value;

        return new FreeAgentTargetLine(
            candidate.Offer.PlayerId.Value,
            playerName,
            candidate.Rationale.Select(finding => ToAILine(finding, teamNames)).ToList(),
            ToSummary(candidate.Assessment, shoppingTeamName, playerName));
    }
}
