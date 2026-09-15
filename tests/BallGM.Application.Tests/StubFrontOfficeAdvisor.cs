using BallGM.Application.AI;
using BallGM.Application.Leagues;
using BallGM.Domain.Common;
using BallGM.Domain.Players;
using BallGM.Domain.Seasons;
using BallGM.Domain.Teams;

namespace BallGM.Application.Tests;

/// <summary>
/// An advisor that assesses nothing, for the same reason <see cref="StubSigningEngine"/> signs
/// nobody: these tests are about session plumbing, and the four AI models are exercised against the
/// real advisor in the rules and integration suites.
/// </summary>
internal sealed class StubFrontOfficeAdvisor : IFrontOfficeAdvisor
{
    public DomainOperationResult<FrontOfficeAssessment> AssessFrontOffice(
        TeamId teamId,
        LeagueSnapshot snapshot,
        StandingsRow? standing) =>
        DomainOperationResult<FrontOfficeAssessment>.Failure(
            new DomainError("test.advisor_not_stubbed", "This test's front-office advisor does not assess anything."));

    public DomainOperationResult<IReadOnlyList<Domain.AI.TradeTargetCandidate>> FindTradeTargets(
        TeamId teamId,
        LeagueSnapshot snapshot) =>
        DomainOperationResult<IReadOnlyList<Domain.AI.TradeTargetCandidate>>.Success([]);

    public DomainOperationResult<IReadOnlyList<Domain.AI.FreeAgentTargetCandidate>> FindFreeAgentTargets(
        TeamId teamId,
        IReadOnlyCollection<Player> freeAgents,
        LeagueSnapshot snapshot) =>
        DomainOperationResult<IReadOnlyList<Domain.AI.FreeAgentTargetCandidate>>.Success([]);

    public DomainOperationResult<DraftPreview?> PreviewDraftRecommendation(
        TeamId teamId,
        LeagueSnapshot snapshot,
        int previewSeed) =>
        DomainOperationResult<DraftPreview?>.Success(null);
}
