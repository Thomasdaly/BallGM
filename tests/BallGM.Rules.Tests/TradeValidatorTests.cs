using BallGM.Domain.Common;
using BallGM.Domain.DraftAssets;
using BallGM.Domain.Trades;
using BallGM.Domain.Transactions;
using BallGM.Rules.Configuration;
using BallGM.Rules.Trades;

namespace BallGM.Rules.Tests;

/// <summary>
/// The failure matrix. Every blocking rule gets a test that fires it and the legal case beside it,
/// because a validator that only ever says no is indistinguishable from a broken one.
/// </summary>
public sealed class TradeValidatorTests
{
    [Fact]
    public void LegalTrade_PassesAndReportsWhatItDoesToBothTeams()
    {
        var league = TradeTestLeague.Build()
            .WithTeam("A", 20_000_000, 10_000_000, 5_000_000)
            .WithTeam("B", 22_000_000, 8_000_000, 4_000_000);

        var assessment = Assess(league, league.SendPlayer("A", 0, "B"), league.SendPlayer("B", 0, "A"));

        Assert.True(assessment.IsLegal, string.Join("; ", assessment.Violations.Select(v => v.Explanation)));

        var teamA = assessment.Outcomes.Single(outcome => outcome.TeamId == league.TeamOf("A").Id);
        Assert.Equal(22_000_000, teamA.IncomingSalary.SmallestUnits);
        Assert.Equal(20_000_000, teamA.OutgoingSalary.SmallestUnits);
        Assert.Equal(35_000_000, teamA.PayrollBefore.SmallestUnits);
        Assert.Equal(37_000_000, teamA.PayrollAfter.SmallestUnits);
        Assert.Equal(3, teamA.RosterCountAfter);
    }

    [Fact]
    public void Validation_LeavesTheLeagueExactlyWhereItFoundIt()
    {
        var league = TradeTestLeague.Build()
            .WithTeam("A", 20_000_000, 10_000_000)
            .WithTeam("B", 22_000_000, 8_000_000);

        var before = league.StateFingerprint();
        Assess(league, league.SendPlayer("A", 0, "B"), league.SendPlayer("B", 0, "A"), league.SendPick("A", 2033, 1, "B"));

        Assert.Equal(before, league.StateFingerprint());
    }

    [Fact]
    public void Trade_IsRejectedWhenATeamSendsAPlayerItDoesNotHave()
    {
        var league = TradeTestLeague.Build()
            .WithTeam("A", 20_000_000, 10_000_000)
            .WithTeam("B", 20_000_000, 10_000_000);

        // B's player, offered by A.
        var movement = TradeAssetMovement.Player(league.PlayerOf("B", 0), league.TeamOf("A").Id, league.TeamOf("B").Id);
        var assessment = Assess(league, movement);

        Assert.False(assessment.IsLegal);
        Assert.Contains(assessment.Violations, violation => violation.RuleCode == "trade.player_not_on_team");
    }

    [Fact]
    public void Trade_IsRejectedWhenAnInjuredPlayerMovesAndTheLeagueForbidsIt()
    {
        var league = TradeTestLeague.Build(injuredEligibility: InjuredPlayerTradeEligibility.Blocked)
            .WithTeam("A", 20_000_000, 10_000_000)
            .WithTeam("B", 20_000_000, 10_000_000)
            .WithInjury("A", 0, "Fractured shooting hand");

        var assessment = Assess(league, league.SendPlayer("A", 0, "B"), league.SendPlayer("B", 0, "A"));

        Assert.False(assessment.IsLegal);
        var violation = Assert.Single(assessment.Violations, finding => finding.RuleCode == "trade.injured_player_not_tradeable");
        Assert.Contains("Fractured shooting hand", violation.Explanation);
    }

    [Fact]
    public void Trade_WarnsRatherThanBlocksWhenTheLeagueAllowsInjuredPlayersToMove()
    {
        var league = TradeTestLeague.Build(injuredEligibility: InjuredPlayerTradeEligibility.AllowedWithWarning)
            .WithTeam("A", 20_000_000, 10_000_000)
            .WithTeam("B", 20_000_000, 10_000_000)
            .WithInjury("A", 0);

        var assessment = Assess(league, league.SendPlayer("A", 0, "B"), league.SendPlayer("B", 0, "A"));

        Assert.True(assessment.IsLegal);
        Assert.Contains(assessment.Warnings, warning => warning.RuleCode == "trade.injured_player_traded");
    }

    [Fact]
    public void Trade_IsRejectedWhenATeamOverTheCapTakesBackMoreSalaryThanItCanMatch()
    {
        // A is over the soft cap, so it has no room to absorb salary and must match instead: 70M back
        // against 50M out is past 125% plus the allowance.
        var league = TradeTestLeague.Build(salaryMatchPercent: 125, salaryMatchAllowance: 1_000_000)
            .WithTeam("A", 60_000_000, 50_000_000)
            .WithTeam("B", 70_000_000, 30_000_000);

        var assessment = Assess(league, league.SendPlayer("A", 1, "B"), league.SendPlayer("B", 0, "A"));

        Assert.False(assessment.IsLegal);
        var violation = Assert.Single(assessment.Violations, finding => finding.RuleCode == "trade.salary_not_matched");
        Assert.Contains("125%", violation.Explanation);
    }

    [Fact]
    public void Trade_IsAllowedWhenTheMatchingPercentageCoversTheDifference()
    {
        var league = TradeTestLeague.Build(salaryMatchPercent: 125, salaryMatchAllowance: 1_000_000)
            .WithTeam("A", 60_000_000, 50_000_000)
            .WithTeam("B", 55_000_000, 30_000_000);

        // 55M back against 50M out is inside 125% plus the allowance.
        var assessment = Assess(league, league.SendPlayer("A", 1, "B"), league.SendPlayer("B", 0, "A"));

        Assert.True(assessment.IsLegal, string.Join("; ", assessment.Violations.Select(v => v.Explanation)));
    }

    [Fact]
    public void Trade_LetsATeamWithRoomUnderTheCapAbsorbSalaryItCouldNeverMatch()
    {
        // B is far under the cap and sends nothing, so matching would forbid this outright — the room
        // allowance is what makes an ordinary salary dump legal.
        var league = TradeTestLeague.Build(minimumRoster: 1, salaryMatchPercent: 125, salaryMatchAllowance: 0)
            .WithTeam("A", 40_000_000, 20_000_000)
            .WithTeam("B", 10_000_000, 10_000_000);

        var assessment = Assess(league, league.SendPlayer("A", 0, "B"));

        Assert.True(assessment.IsLegal, string.Join("; ", assessment.Violations.Select(v => v.Explanation)));
    }

    [Fact]
    public void Trade_IsRejectedWhenItWouldPushATeamPastTheHardCap()
    {
        var league = TradeTestLeague.Build(salaryMatchPercent: 500, salaryMatchAllowance: 100_000_000)
            .WithTeam("A", 80_000_000, 60_000_000)
            .WithTeam("B", 90_000_000, 5_000_000);

        var assessment = Assess(league, league.SendPlayer("A", 1, "B"), league.SendPlayer("B", 0, "A"));

        Assert.False(assessment.IsLegal);
        Assert.Contains(assessment.Violations, violation => violation.RuleCode == "trade.hard_cap_exceeded");
    }

    [Fact]
    public void Trade_IsRejectedWhenATeamAboveTheSecondApronTakesOnAnyNetSalary()
    {
        var league = TradeTestLeague.Build(salaryMatchPercent: 500, salaryMatchAllowance: 100_000_000)
            .WithTeam("A", 80_000_000, 20_000_000)
            .WithTeam("B", 65_000_000, 10_000_000);

        // A finishes at 145M, above the second apron (140M), and takes back more than it sends.
        var assessment = Assess(league, league.SendPlayer("A", 1, "B"), league.SendPlayer("B", 0, "A"));

        Assert.False(assessment.IsLegal);
        Assert.Contains(assessment.Violations, violation => violation.RuleCode == "trade.second_apron_salary_increase");
    }

    [Fact]
    public void Trade_IsAllowedAboveTheSecondApronWhenTheRulesetDoesNotRestrictIt()
    {
        var unrestricted = TradeRules.Create(
            salaryMatchPercent: 500,
            new Money(100_000_000),
            InjuredPlayerTradeEligibility.Allowed,
            secondApronBlocksSalaryIncrease: false).Value;

        var league = TradeTestLeague.Build(salaryMatchPercent: 500, salaryMatchAllowance: 100_000_000)
            .WithTeam("A", 80_000_000, 20_000_000)
            .WithTeam("B", 65_000_000, 10_000_000)
            .WithTradeRules(unrestricted);

        var assessment = Assess(league, league.SendPlayer("A", 1, "B"), league.SendPlayer("B", 0, "A"));

        Assert.DoesNotContain(assessment.Violations, violation => violation.RuleCode == "trade.second_apron_salary_increase");
    }

    [Fact]
    public void Trade_IsRejectedWhenItWouldOverfillARoster()
    {
        var league = TradeTestLeague.Build(minimumRoster: 1, maximumRoster: 3)
            .WithTeam("A", 10_000_000, 9_000_000, 8_000_000)
            .WithTeam("B", 10_000_000, 9_000_000, 8_000_000);

        var assessment = Assess(
            league,
            league.SendPlayer("A", 0, "B"),
            league.SendPlayer("A", 1, "B"));

        Assert.False(assessment.IsLegal);
        Assert.Contains(assessment.Violations, violation => violation.RuleCode == "trade.roster_maximum_exceeded");
    }

    [Fact]
    public void Trade_IsRejectedWhenItWouldLeaveARosterBelowItsMinimum()
    {
        var league = TradeTestLeague.Build(minimumRoster: 2, maximumRoster: 5)
            .WithTeam("A", 10_000_000, 9_000_000)
            .WithTeam("B", 10_000_000, 9_000_000);

        var assessment = Assess(
            league,
            league.SendPlayer("A", 0, "B"),
            league.SendPlayer("A", 1, "B"));

        Assert.False(assessment.IsLegal);
        Assert.Contains(assessment.Violations, violation => violation.RuleCode == "trade.roster_minimum_not_met");
    }

    [Fact]
    public void Trade_IsRejectedWhenATeamSendsAPickItDoesNotControl()
    {
        var league = TradeTestLeague.Build()
            .WithTeam("A", 10_000_000, 9_000_000)
            .WithTeam("B", 10_000_000, 9_000_000);

        // B's pick, offered by A.
        var movement = TradeAssetMovement.DraftPick(league.PickOf("B", 2033, 1), league.TeamOf("A").Id, league.TeamOf("B").Id);
        var assessment = Assess(league, movement);

        Assert.False(assessment.IsLegal);
        Assert.Contains(assessment.Violations, violation => violation.RuleCode == "pick_transfer.not_controlled");
    }

    [Fact]
    public void Trade_IsRejectedWhenThePickIsAlreadyPromisedToSomebodyElse()
    {
        var league = TradeTestLeague.Build()
            .WithTeam("A", 10_000_000, 9_000_000)
            .WithTeam("B", 10_000_000, 9_000_000);

        var protection = PickProtection.TopSelections([4], PickProtectionFallback.Extinguishes).Value;
        league.DraftAssets.Encumber(
            league.PickOf("A", 2033, 1),
            new PickObligation(new PickEncumbranceId("ENCUMBRANCE-1"), league.FranchiseOf("B"), protection));

        var assessment = Assess(league, league.SendPick("A", 2033, 1, "B"));

        Assert.False(assessment.IsLegal);
        Assert.Contains(assessment.Violations, violation => violation.RuleCode == "pick_transfer.conflicting_encumbrance");
    }

    [Fact]
    public void Trade_IsRejectedWhenItBreaksTheConsecutiveFutureDraftRestriction()
    {
        var league = TradeTestLeague.Build()
            .WithTeam("A", 10_000_000, 9_000_000)
            .WithTeam("B", 10_000_000, 9_000_000);

        // A's 2033 first is already gone, so giving up 2032 leaves that pair of drafts empty.
        league.DraftAssets.Transfer(league.PickOf("A", 2033, 1), league.FranchiseOf("B"));

        var assessment = Assess(league, league.SendPick("A", 2032, 1, "B"));

        Assert.False(assessment.IsLegal);
        Assert.Contains(assessment.Violations, violation => violation.RuleCode == "pick_transfer.retained_round_restriction");
    }

    [Fact]
    public void Trade_IsRejectedWhenTheLeagueHasMovedOnSinceTheProposalWasBuilt()
    {
        var league = TradeTestLeague.Build()
            .WithTeam("A", 20_000_000, 10_000_000)
            .WithTeam("B", 20_000_000, 10_000_000);

        var proposal = league.Proposal(league.SendPlayer("A", 0, "B"), league.SendPlayer("B", 0, "A"));
        league.Ledger.Record(TransactionKind.ContractSigned, TradeTestLeague.CurrentSeason, league.TeamOf("A").Id, "Someone signed.");

        var assessment = new TradeValidator().Validate(proposal, league.Context()).Value;

        Assert.False(assessment.IsLegal);
        Assert.Contains(assessment.Violations, violation => violation.RuleCode == "trade.stale_proposal");
    }

    [Fact]
    public void Trade_WarnsWhenItTakesATeamOverTheTaxLine()
    {
        var league = TradeTestLeague.Build(salaryMatchPercent: 500, salaryMatchAllowance: 100_000_000)
            .WithTeam("A", 60_000_000, 10_000_000)
            .WithTeam("B", 65_000_000, 10_000_000);

        // A goes from 70M to 125M, crossing the 120M tax line.
        var assessment = Assess(league, league.SendPlayer("A", 1, "B"), league.SendPlayer("B", 0, "A"));

        Assert.Contains(assessment.Warnings, warning => warning.RuleCode == "trade.crosses_luxury_tax");
    }

    [Fact]
    public void ThreeTeamTrade_IsAssessedTeamByTeam()
    {
        var league = TradeTestLeague.Build()
            .WithTeam("A", 20_000_000, 10_000_000)
            .WithTeam("B", 21_000_000, 10_000_000)
            .WithTeam("C", 22_000_000, 10_000_000);

        var assessment = Assess(
            league,
            league.SendPlayer("A", 0, "B"),
            league.SendPlayer("B", 0, "C"),
            league.SendPlayer("C", 0, "A"));

        Assert.True(assessment.IsLegal, string.Join("; ", assessment.Violations.Select(v => v.Explanation)));
        Assert.Equal(3, assessment.Outcomes.Count);
        Assert.All(assessment.Outcomes, outcome => Assert.Equal(2, outcome.RosterCountAfter));
    }

    [Fact]
    public void Trade_ReportsPickCountsSoAssetMovementIsVisibleWithoutTheBoard()
    {
        var league = TradeTestLeague.Build()
            .WithTeam("A", 20_000_000, 10_000_000)
            .WithTeam("B", 20_000_000, 10_000_000);

        var assessment = Assess(league, league.SendPick("A", 2034, 2, "B"));

        var teamA = assessment.Outcomes.Single(outcome => outcome.TeamId == league.TeamOf("A").Id);
        Assert.Equal(teamA.PicksBefore - 1, teamA.PicksAfter);
    }

    private static TradeAssessment Assess(TradeTestLeague league, params TradeAssetMovement[] movements)
    {
        var proposal = league.Proposal(movements);
        var result = new TradeValidator().Validate(proposal, league.Context());

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));
        return result.Value;
    }
}
