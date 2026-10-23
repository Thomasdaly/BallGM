using BallGM.Application.Leagues;
using BallGM.Application.Negotiations;
using BallGM.Domain.Common;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Players;
using BallGM.Domain.Seasons;
using BallGM.Domain.Teams;

namespace BallGM.Application.Tests;

/// <summary>
/// A signing engine that signs nobody. The Application tests are about projection and session
/// plumbing, not about signing rules — those are exercised against the real engine in the rules and
/// integration suites — so this stub refuses everything loudly rather than pretending to judge.
/// </summary>
internal sealed class StubSigningEngine : ISigningEngine
{
    public DomainOperationResult<ExtensionAssessment> AssessExtension(Offer offer, LeagueSnapshot snapshot) =>
        DomainOperationResult<ExtensionAssessment>.Failure(new DomainError("stub.no_extensions", "The stub signing engine does not assess extensions."));

    public DomainOperationResult<ExtensionResult> ExecuteExtension(Offer offer, LeagueSnapshot snapshot) =>
        DomainOperationResult<ExtensionResult>.Failure(new DomainError("stub.no_extensions", "The stub signing engine does not execute extensions."));

    public DomainOperationResult<SigningAssessment> Assess(
        Offer offer,
        LeagueSnapshot snapshot,
        TeamId teamId,
        PlayerId playerId,
        SeasonDay? day = null) =>
        DomainOperationResult<SigningAssessment>.Failure(
            new DomainError("test.signing_not_stubbed", "This test's signing engine does not assess offers."));

    public DomainOperationResult<SigningResult> Execute(
        Offer offer,
        LeagueSnapshot snapshot,
        TeamId teamId,
        PlayerId playerId,
        SeasonDay? day = null) =>
        DomainOperationResult<SigningResult>.Failure(
            new DomainError("test.signing_not_stubbed", "This test's signing engine does not execute offers."));

    /// <summary>No league in these fixtures configures a floor or a ceiling, so neither applies.</summary>
    public CompensationLimits LimitsFor(LeagueSnapshot snapshot, int seasonsOfService) => new(null, null);

    public DomainOperationResult ExecuteBuyout(LeagueSnapshot snapshot, TeamId teamId, Domain.Players.PlayerId playerId, Domain.Contracts.ContractId contractId, int retainedPercent) =>
        DomainOperationResult.Success;

    public DomainOperationResult<Domain.Contracts.Contract> ExecuteRookieContract(LeagueSnapshot snapshot, TeamId teamId, Domain.Players.PlayerId playerId, int round, int selection) =>
        DomainOperationResult<Domain.Contracts.Contract>.Failure(new DomainError("stub.no_rookie_scale", "The stub signs no rookies."));

    public DomainOperationResult DecideOption(LeagueSnapshot snapshot, Domain.Contracts.ContractId contractId, Domain.Leagues.Season season, bool exercise) =>
        DomainOperationResult.Success;
}
