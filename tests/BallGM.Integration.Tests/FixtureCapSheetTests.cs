using BallGM.Application.Leagues;
using BallGM.Infrastructure.Cap;
using BallGM.Infrastructure.DraftAssets;
using BallGM.Infrastructure.Fixtures;
using BallGM.Infrastructure.Negotiations;

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
            Assert.Equal(capSheet.CommittedSalary + capSheet.DeadMoney + capSheet.RosterHolds, capSheet.TotalPayroll);
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
        // Contracts plus two roster-slot holds: this team is two players short of the league minimum,
        // and the payroll every threshold is measured against says so.
        Assert.Equal(118_400_000 + 2_300_000, PayrollOf(overview, "Old Foundry Bellringers"));
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

    /// <summary>
    /// The room a short-handed team is shown is room it can actually spend. Without the holds this
    /// team would appear to have the whole gap to the soft cap available, and would find out only
    /// after spending it that two roster spots still had to be filled.
    /// </summary>
    [Fact]
    public void TheTeamUnderTheSoftCapIsReportedWithRoomLeftNetOfTheSpotsItStillHasToFill()
    {
        var team = TeamNamed(LoadShippedLeague(), "Old Foundry Bellringers");
        var softCap = team.CapSheet.Thresholds.Single(threshold => threshold.ThresholdName == "Soft cap");

        Assert.Equal("cap.under_soft_cap", softCap.RuleCode);
        Assert.Equal(2_300_000, team.CapSheet.RosterHolds);
        Assert.Equal(141_000_000 - 118_400_000 - 2_300_000, softCap.SignedDistance);
        Assert.False(softCap.IsOver);
    }

    /// <summary>
    /// One hold per unfilled spot rather than one lumped figure, each priced at the league's minimum
    /// salary for a player with no service — the least the team can possibly spend to fill it.
    /// </summary>
    [Fact]
    public void AShortHandedTeamCarriesOneHoldPerUnfilledSpotAtTheLeagueMinimum()
    {
        var team = TeamNamed(LoadShippedLeague(), "Old Foundry Bellringers");
        var holds = team.CapSheet.Charges.Where(charge => charge.Kind == "Roster-slot hold").ToList();

        Assert.Equal(2, holds.Count);
        Assert.All(holds, hold =>
        {
            Assert.Equal(1_150_000, hold.Amount);
            Assert.Equal("Unfilled roster spot", hold.PlayerName);
        });
    }

    /// <summary>
    /// A team at the roster minimum holds nothing. The hold prices an obligation, and a team that has
    /// met the obligation has none — a charge of nought on the sheet would say otherwise.
    /// </summary>
    [Fact]
    public void ATeamAtTheRosterMinimumCarriesNoHolds()
    {
        var team = TeamNamed(LoadShippedLeague(), "Northreach Aurora");

        Assert.Equal(12, team.RosterCount);
        Assert.Equal(0, team.CapSheet.RosterHolds);
        Assert.DoesNotContain(team.CapSheet.Charges, charge => charge.Kind == "Roster-slot hold");
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
                overview.CapThresholds.PayrollFloor,
                overview.CapThresholds.SoftCap,
                overview.CapThresholds.LuxuryTax,
                overview.CapThresholds.FirstApron,
                overview.CapThresholds.SecondApron,
                overview.CapThresholds.HardCap,
            ],
            team.CapSheet.Thresholds.Select(threshold => (long?)threshold.ThresholdAmount));
    }

    private static long PayrollOf(LeagueOverview overview, string teamName) =>
        TeamNamed(overview, teamName).CapSheet.TotalPayroll;

    private static string RuleCodeFor(TeamSummary team, string thresholdName) =>
        team.CapSheet.Thresholds.Single(threshold => threshold.ThresholdName == thresholdName).RuleCode;

    private static TeamSummary TeamNamed(LeagueOverview overview, string teamName) =>
        overview.Teams.Single(team => team.TeamName == teamName);

    private static LeagueOverview LoadShippedLeague()
    {
        var result = new GetLeagueOverviewQuery(new FixtureLeagueDataSource(), new RulesCapLedger(), new RulesDraftAssetLedger(), new RulesSigningEngine()).Execute();

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));
        return result.Value;
    }
}
