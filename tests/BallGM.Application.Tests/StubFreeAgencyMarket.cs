using BallGM.Application.Leagues;
using BallGM.Application.Negotiations;
using BallGM.Domain.Common;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Players;
using BallGM.Domain.Randomness;

namespace BallGM.Application.Tests;

/// <summary>
/// A market that resolves nothing, for the same reason <see cref="StubSigningEngine"/> signs nobody:
/// these tests are about session plumbing, and the resolution rules are exercised against the real
/// resolver in the rules and integration suites.
/// </summary>
internal sealed class StubFreeAgencyMarket : IFreeAgencyMarket
{
    public DomainOperationResult<MarketAssessment> Assess(
        Negotiation negotiation,
        LeagueSnapshot snapshot,
        SeasonDay day,
        IRandomSource random) =>
        DomainOperationResult<MarketAssessment>.Failure(
            new DomainError("test.market_not_stubbed", "This test's free-agency market does not assess offers."));

    public DomainOperationResult<MarketExecution> Resolve(
        Negotiation negotiation,
        LeagueSnapshot snapshot,
        SeasonDay day,
        IRandomSource random) =>
        DomainOperationResult<MarketExecution>.Failure(
            new DomainError("test.market_not_stubbed", "This test's free-agency market does not resolve markets."));

    /// <summary>No league in these fixtures configures a floor or a ceiling, so nobody has an ask.</summary>
    public Money? AskingPrice(LeagueSnapshot snapshot, PlayerId playerId) => null;
}
