using BallGM.Application.Leagues;
using BallGM.Infrastructure.Cap;
using BallGM.Infrastructure.DraftAssets;
using BallGM.Infrastructure.Fixtures;

namespace BallGM.Integration.Tests;

/// <summary>
/// The cap path end to end: a ruleset file on disk, contracts generated against it, charges
/// projected from those contracts, and the Rules cap ledger comparing the total to every configured
/// threshold. The fixture's payroll spread is asserted here on purpose — a UI that has only ever
/// been looked at for a comfortable team has not been exercised against the rules.
/// </summary>
public sealed class FixtureCapSheetTests
{
    [Fact]
    public void EveryTeamsPayrollIsTheSumOfItsOwnCharges()
    {
        var overview = LoadShippedLeague();

        Assert.All(overview.Teams, team =>
        {
            var capSheet = team.CapSheet;
            Assert.Equal(capSheet.TotalPayroll, capSheet.Charges.Sum(charge => charge.Amount));
            Assert.Equal(capSheet.CommittedSalary + capSheet.DeadMoney, capSheet.TotalPayroll);
            Assert.Equal(overview.SeasonYear, capSheet.SeasonYear);
        });
    }

    [Fact]
    public void TheLeagueSpreadsTeamsAcrossEveryThresholdBand()
    {
        var overview = LoadShippedLeague();

        Assert.Equal(198_000_000, PayrollOf(overview, "Harbourline Tidewatch"));
        Assert.Equal(181_500_000, PayrollOf(overview, "Cascade Falls Ironworks"));
        Assert.Equal(175_000_000, PayrollOf(overview, "Verdanmoor Kestrels"));
        Assert.Equal(168_000_000, PayrollOf(overview, "Saltpan City Prospectors"));
        Assert.Equal(141_000_000, PayrollOf(overview, "Northreach Aurora"));
        Assert.Equal(118_400_000, PayrollOf(overview, "Old Foundry Bellringers"));
    }

    [Fact]
    public void TheTeamOverTheSecondApronIsReportedThatWayAndStillUnderTheHardCap()
    {
        var team = TeamNamed(LoadShippedLeague(), "Harbourline Tidewatch");

        Assert.Equal("cap.over_second_apron", RuleCodeFor(team, "Second apron"));
        Assert.Equal("cap.under_hard_cap", RuleCodeFor(team, "Hard cap"));
    }

    [Fact]
    public void TheMidTaxTeamIsOverTheTaxLineAndUnderBothAprons()
    {
        var team = TeamNamed(LoadShippedLeague(), "Verdanmoor Kestrels");

        Assert.Equal("cap.over_luxury_tax", RuleCodeFor(team, "Luxury tax"));
        Assert.Equal("cap.under_first_apron", RuleCodeFor(team, "First apron"));
        Assert.Equal("cap.under_second_apron", RuleCodeFor(team, "Second apron"));
    }

    [Fact]
    public void TheTeamUnderTheSoftCapIsReportedWithRoomLeft()
    {
        var team = TeamNamed(LoadShippedLeague(), "Old Foundry Bellringers");
        var softCap = team.CapSheet.Thresholds.Single(threshold => threshold.ThresholdName == "Soft cap");

        Assert.Equal("cap.under_soft_cap", softCap.RuleCode);
        Assert.Equal(141_000_000 - 118_400_000, softCap.SignedDistance);
        Assert.False(softCap.IsOver);
    }

    [Fact]
    public void ATeamSittingExactlyOnTheSoftCapIsReportedAtTheLineRatherThanEitherSideOfIt()
    {
        var team = TeamNamed(LoadShippedLeague(), "Northreach Aurora");
        var softCap = team.CapSheet.Thresholds.Single(threshold => threshold.ThresholdName == "Soft cap");

        Assert.Equal("cap.at_soft_cap", softCap.RuleCode);
        Assert.Equal(0, softCap.SignedDistance);
    }

    [Fact]
    public void DeadMoneyIsCarriedByAPlayerWhoIsNoLongerOnTheRoster()
    {
        var team = TeamNamed(LoadShippedLeague(), "Saltpan City Prospectors");

        Assert.Equal(7_200_000, team.CapSheet.DeadMoney);

        var deadMoney = Assert.Single(team.CapSheet.Charges, charge => charge.IsDeadMoney);
        Assert.Equal("Dead money", deadMoney.Kind);
        Assert.Equal(7_200_000, deadMoney.Amount);
        Assert.DoesNotContain(team.Roster, spot => spot.FullName == deadMoney.PlayerName);
    }

    [Fact]
    public void EveryRosteredPlayerCostsSomethingAgainstTheCurrentSeason()
    {
        var overview = LoadShippedLeague();

        Assert.All(overview.Teams, team => Assert.All(team.Roster, spot =>
        {
            Assert.True(spot.CapCharge > 0, $"{spot.FullName} has no cap charge.");

            // Fixture deals run two to four seasons; one team's declined option ends a deal a
            // season early, which is what leaves a single season on one contract.
            Assert.InRange(spot.ContractSeasonsRemaining, 1, 4);
        }));
    }

    [Fact]
    public void EveryTeamsPayrollHasALedgerBehindIt()
    {
        var overview = LoadShippedLeague();

        Assert.All(overview.Teams, team =>
        {
            Assert.NotEmpty(team.CapSheet.Transactions);
            Assert.All(team.CapSheet.Transactions, line => Assert.False(string.IsNullOrWhiteSpace(line.Reason)));
        });

        var released = TeamNamed(overview, "Old Foundry Bellringers").CapSheet.Transactions;
        Assert.Contains(released, line => line.Kind == "Player released" && line.Amount == 3_500_000);
    }

    [Fact]
    public void ThresholdsAreReportedFromTheRulesetFileRatherThanFromCode()
    {
        var overview = LoadShippedLeague();
        var team = overview.Teams[0];

        Assert.Equal(
            [
                overview.CapThresholds.SoftCap,
                overview.CapThresholds.LuxuryTax,
                overview.CapThresholds.FirstApron,
                overview.CapThresholds.SecondApron,
                overview.CapThresholds.HardCap,
            ],
            team.CapSheet.Thresholds.Select(threshold => threshold.ThresholdAmount));
    }

    private static long PayrollOf(LeagueOverview overview, string teamName) =>
        TeamNamed(overview, teamName).CapSheet.TotalPayroll;

    private static string RuleCodeFor(TeamSummary team, string thresholdName) =>
        team.CapSheet.Thresholds.Single(threshold => threshold.ThresholdName == thresholdName).RuleCode;

    private static TeamSummary TeamNamed(LeagueOverview overview, string teamName) =>
        overview.Teams.Single(team => team.TeamName == teamName);

    private static LeagueOverview LoadShippedLeague()
    {
        var result = new GetLeagueOverviewQuery(new FixtureLeagueDataSource(), new RulesCapLedger(), new RulesDraftAssetLedger()).Execute();

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));
        return result.Value;
    }
}
