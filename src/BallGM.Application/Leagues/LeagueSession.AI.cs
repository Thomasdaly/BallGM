using BallGM.Application.AI;
using BallGM.Domain.AI;
using BallGM.Domain.Common;
using BallGM.Domain.Players;
using BallGM.Domain.Seasons;
using BallGM.Domain.Teams;
using BallGM.Domain.Trades;

namespace BallGM.Application.Leagues;

/// <summary>
/// The Milestone 9 diagnostics half of the session: what a team's own front office would read and do,
/// through <see cref="IFrontOfficeAdvisor"/>. Every read here is exactly that — a read. Nothing in
/// this file proposes a trade, places an offer, or drafts anyone; <see cref="AssessTrade"/>,
/// <see cref="SubmitOffer"/> and the draft the season boundary runs automatically remain the only
/// ways anything here actually happens. See <c>IFrontOfficeAdvisor</c>'s own remarks for why that
/// split is deliberate.
/// </summary>
public sealed partial class LeagueSession
{
    private const string AIUnknownTeamCode = "ai_advisory.unknown_team";

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
