using BallGM.Domain.AI;
using BallGM.Domain.Common;
using BallGM.Domain.Players;
using BallGM.Domain.Teams;
using BallGM.Rules.AI;
using BallGM.Rules.Configuration;

namespace BallGM.Rules.Tests.AI;

public sealed class TradeTargetingModelTests
{
    private static readonly DevelopmentRules PeakRules =
        DevelopmentRules.Create(peakAgeStart: 24, peakAgeEnd: 29, growthCurve: null, declineCurve: null, varianceRange: 0).Value;

    private static readonly NegotiationRules OpenRules = NegotiationRules.Create(
        CapThresholds.Uncapped,
        maximumContractSeasons: null,
        maximumIncumbentContractSeasons: null,
        maximumAnnualEscalationPercent: null,
        maximumAnnualDeescalationPercent: null,
        CompensationCeilingScale.None,
        CompensationFloorScale.None,
        standardOverCapAllowance: null,
        standardOverCapAllowanceUnavailableAbove: null,
        allowanceMaySplitAcrossPlayers: false,
        Domain.Negotiations.MarketResolutionMode.ResolutionPoint,
        offerExpiryDays: null).Value;

    /// <summary>
    /// Every roster below covers all five positions naturally before adding any "surplus" player —
    /// <c>DepthChartBuilder</c> substitutes an out-of-position player into an uncovered spot rather
    /// than leaving it empty, so a roster with a genuine gap at one position and true depth at another
    /// needs all five positions filled first, or the builder quietly reassigns the "surplus" player to
    /// cover the gap instead of benching them.
    /// </summary>
    [Fact]
    public void FindsAMutualNeedsMatchBetweenTwoTeamsSpareDepth()
    {
        var league = TradeTargetingTestLeague.Build()
            .WithTeam(
                "A",
                (Position.PointGuard, 75, 8_000_000),
                (Position.ShootingGuard, 70, 6_000_000),
                (Position.SmallForward, 70, 6_000_000),
                (Position.PowerForward, 70, 6_000_000),
                (Position.Center, 40, 3_000_000), // A's need: a weak Center starter.
                (Position.PointGuard, 65, 3_000_000)) // A's surplus: a spare point guard.
            .WithTeam(
                "B",
                (Position.PointGuard, 40, 3_000_000), // B's need: a weak point guard starter.
                (Position.ShootingGuard, 70, 6_000_000),
                (Position.SmallForward, 70, 6_000_000),
                (Position.PowerForward, 70, 6_000_000),
                (Position.Center, 72, 8_000_000),
                (Position.Center, 68, 3_000_000)); // B's surplus: a spare centre.

        var candidates = TradeTargetingModel.FindCandidates(league.TeamId("A"), league.Context(), PeakRules, OpenRules);

        var candidate = Assert.Single(candidates);
        Assert.True(candidate.Assessment.IsLegal, string.Join("; ", candidate.Assessment.Violations.Select(v => v.Explanation)));
        Assert.Equal(league.TeamId("A"), candidate.ShoppingTeamId);
        Assert.Equal(league.TeamId("B"), candidate.CounterpartyTeamId);

        var incoming = candidate.Proposal.Movements.Single(movement => movement.ToTeamId == league.TeamId("A"));
        var outgoing = candidate.Proposal.Movements.Single(movement => movement.ToTeamId == league.TeamId("B"));
        Assert.Equal(league.PlayerId("B", 5), incoming.PlayerId);
        Assert.Equal(league.PlayerId("A", 5), outgoing.PlayerId);

        Assert.Contains(candidate.Rationale, finding => finding.RuleCode == "ai_trade_target.needs_match");
        Assert.Contains(candidate.Rationale, finding => finding.RuleCode == "ai_trade_target.value_parity");
    }

    [Fact]
    public void InASummerMarket_ATeamThatHasAlreadyTradedOrAPlayerAlreadyMovedIsOffTheTable()
    {
        var league = TradeTargetingTestLeague.Build()
            .WithTeam("A", (Position.PointGuard, 75, 8_000_000), (Position.ShootingGuard, 70, 6_000_000), (Position.SmallForward, 70, 6_000_000), (Position.PowerForward, 70, 6_000_000), (Position.Center, 40, 3_000_000), (Position.PointGuard, 65, 3_000_000))
            .WithTeam("B", (Position.PointGuard, 40, 3_000_000), (Position.ShootingGuard, 70, 6_000_000), (Position.SmallForward, 70, 6_000_000), (Position.PowerForward, 70, 6_000_000), (Position.Center, 72, 8_000_000), (Position.Center, 68, 3_000_000));
        var directions = new Dictionary<TeamId, CompetitiveDirection>();
        var asOf = new DateOnly(2031, 7, 1);

        // Both sides gain (each fills a forty-rated hole with a spare), so the open market finds it ...
        var open = new TradeTargetingOptions(new HashSet<TeamId>(), new HashSet<PlayerId>(), directions, asOf);
        Assert.Contains(TradeTargetingModel.FindCandidates(league.TeamId("A"), league.Context(), PeakRules, OpenRules, open)[0].Rationale, finding => finding.RuleCode == "ai_trade_target.both_sides_gain");

        // ... but not once B has traded this summer, or once A's spare has already been moved.
        var traded = open with { ExcludedTeams = new HashSet<TeamId> { league.TeamId("B") } };
        Assert.Empty(TradeTargetingModel.FindCandidates(league.TeamId("A"), league.Context(), PeakRules, OpenRules, traded));
        var moved = open with { ExcludedPlayers = new HashSet<PlayerId> { league.PlayerId("A", 5) } };
        Assert.Empty(TradeTargetingModel.FindCandidates(league.TeamId("A"), league.Context(), PeakRules, OpenRules, moved));
    }

    [Fact]
    public void FindsNoCandidateWhenTheCounterpartyHasNoSurplusAtTheNeededPosition()
    {
        var league = TradeTargetingTestLeague.Build()
            .WithTeam(
                "A",
                (Position.PointGuard, 75, 8_000_000),
                (Position.ShootingGuard, 70, 6_000_000),
                (Position.SmallForward, 70, 6_000_000),
                (Position.PowerForward, 70, 6_000_000),
                (Position.Center, 40, 3_000_000)) // A's need: a weak Center starter.
            .WithTeam(
                "B",
                (Position.PointGuard, 40, 3_000_000),
                (Position.ShootingGuard, 70, 6_000_000),
                (Position.SmallForward, 70, 6_000_000),
                (Position.PowerForward, 70, 6_000_000),
                (Position.Center, 72, 8_000_000)); // B's only centre — no backup to spare.

        var candidates = TradeTargetingModel.FindCandidates(league.TeamId("A"), league.Context(), PeakRules, OpenRules);

        Assert.Empty(candidates);
    }

    [Fact]
    public void FindsNoCandidateWhenTheTwoPlayersFallOutsideTheParityTolerance()
    {
        var league = TradeTargetingTestLeague.Build()
            .WithTeam(
                "A",
                (Position.PointGuard, 75, 8_000_000),
                (Position.ShootingGuard, 70, 6_000_000),
                (Position.SmallForward, 70, 6_000_000),
                (Position.PowerForward, 70, 6_000_000),
                (Position.Center, 30, 1_000_000), // A's need: a weak Center starter.
                (Position.PointGuard, 20, 1_000_000)) // A's surplus: a very weak spare guard.
            .WithTeam(
                "B",
                (Position.PointGuard, 30, 1_000_000), // B's need: a weak point guard starter.
                (Position.ShootingGuard, 70, 6_000_000),
                (Position.SmallForward, 70, 6_000_000),
                (Position.PowerForward, 70, 6_000_000),
                (Position.Center, 90, 20_000_000),
                (Position.Center, 90, 20_000_000)); // B's surplus: a star-level spare centre.

        var candidates = TradeTargetingModel.FindCandidates(league.TeamId("A"), league.Context(), PeakRules, OpenRules);

        Assert.Empty(candidates);
    }

    [Fact]
    public void ExcludesACandidateThatWouldBreachTheHardCap()
    {
        var thresholds = CapThresholds.Create(hardCap: new Money(35_000_000)).Value;
        var league = TradeTargetingTestLeague.Build(capThresholds: thresholds)
            .WithTeam(
                "A",
                (Position.PointGuard, 75, 8_000_000),
                (Position.ShootingGuard, 70, 6_000_000),
                (Position.SmallForward, 70, 6_000_000),
                (Position.PowerForward, 70, 6_000_000),
                (Position.Center, 40, 3_000_000),
                (Position.PointGuard, 65, 3_000_000)) // Sent out for $3M.
            .WithTeam(
                "B",
                (Position.PointGuard, 40, 3_000_000),
                (Position.ShootingGuard, 70, 6_000_000),
                (Position.SmallForward, 70, 6_000_000),
                (Position.PowerForward, 70, 6_000_000),
                (Position.Center, 72, 8_000_000),
                (Position.Center, 68, 10_000_000)); // Received for $10M — pushes A's payroll from $32M to $39M, over the $35M hard cap.

        var candidates = TradeTargetingModel.FindCandidates(league.TeamId("A"), league.Context(), PeakRules, OpenRules);

        Assert.Empty(candidates);
    }

    /// <summary>
    /// Regression: a player who was released (their old contract kept alive only for its dead-money
    /// terms, per <c>LeagueSnapshot</c>'s own remarks on why it can carry someone no roster references)
    /// and later re-signed elsewhere ends up with two contracts whose stated terms both reach the
    /// current season — <c>Contract.TermFor</c> answers "is this season in the contract," not "is the
    /// contract still live," so a naive <c>!IsTerminated</c>-only or <c>TermFor</c>-only reading throws
    /// building a per-player dictionary. This must key on both together, the same pair
    /// <c>LeagueSession.IsFreeAgent</c> already reads.
    /// </summary>
    [Fact]
    public void DoesNotThrowWhenAPlayerHoldsBothAReleasedContractAndAFreshOne()
    {
        var league = TradeTargetingTestLeague.Build()
            .WithTeam(
                "A",
                (Position.PointGuard, 75, 8_000_000),
                (Position.ShootingGuard, 70, 6_000_000),
                (Position.SmallForward, 70, 6_000_000),
                (Position.PowerForward, 70, 6_000_000),
                (Position.Center, 40, 3_000_000),
                (Position.PointGuard, 65, 3_000_000))
            .WithTeam(
                "B",
                (Position.PointGuard, 40, 3_000_000),
                (Position.ShootingGuard, 70, 6_000_000),
                (Position.SmallForward, 70, 6_000_000),
                (Position.PowerForward, 70, 6_000_000),
                (Position.Center, 72, 8_000_000),
                (Position.Center, 68, 3_000_000))
            .WithTerminatedContractStillCoveringTheCurrentSeason("B", 5);

        var candidates = TradeTargetingModel.FindCandidates(league.TeamId("A"), league.Context(), PeakRules, OpenRules);

        Assert.Single(candidates);
    }

    [Fact]
    public void ReturnsNoCandidatesForATeamNotInTheLeague()
    {
        var league = TradeTargetingTestLeague.Build()
            .WithTeam("A", (Position.PointGuard, 75, 8_000_000));

        var candidates = TradeTargetingModel.FindCandidates(new TeamId("TEAM-MISSING"), league.Context(), PeakRules, OpenRules);

        Assert.Empty(candidates);
    }
}
