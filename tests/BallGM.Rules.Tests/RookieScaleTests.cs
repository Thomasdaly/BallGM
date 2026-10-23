using BallGM.Domain.Common;
using BallGM.Domain.Contracts;
using BallGM.Domain.Franchises;
using BallGM.Domain.Leagues;
using BallGM.Domain.Players;
using BallGM.Domain.Teams;
using BallGM.Domain.Transactions;
using BallGM.Rules.AI;
using BallGM.Rules.Configuration;
using BallGM.Rules.Signings;

namespace BallGM.Rules.Tests;

/// <summary>The rookie scale and the option decisions that follow it (<c>docs/franchise-realism.md</c> → F3).</summary>
public sealed class RookieScaleTests
{
    private static readonly Season Season = new(2031);
    private static readonly Money Cap = new(100_000_000);
    private static readonly Money Minimum = new(1_000_000);

    private static RookieScaleRules Scale() => RookieScaleRules.Create(
        BandedScale.Create([new ScaleBand(0, 850), new ScaleBand(2, 600), new ScaleBand(10, 300), new ScaleBand(25, 180)]).Value,
        seasons: 4,
        teamOptionSeasons: 2,
        secondRoundSeasons: 2).Value;

    [Fact]
    public void TheFirstPickEarnsTheTopShare_AndTheLastTwoSeasonsAreTheTeamsOptions()
    {
        var terms = Scale().TermsFor(round: 1, selection: 1, Season, Cap, Minimum)!;

        Assert.Equal(4, terms.Count);
        Assert.Equal(8_500_000, terms[0].Compensation.SmallestUnits);
        Assert.True(terms[3].Compensation > terms[0].Compensation);
        Assert.Equal([ContractOptionKind.None, ContractOptionKind.None, ContractOptionKind.Team, ContractOptionKind.Team], terms.Select(term => term.Option));
        Assert.True(Scale().TermsFor(1, 30, Season, Cap, Minimum)![0].Compensation < terms[0].Compensation);
    }

    [Fact]
    public void ASecondRoundPickSignsAtTheMinimum_AndAnUnconfiguredLeagueHasNoScale()
    {
        var terms = Scale().TermsFor(round: 2, selection: 45, Season, Cap, Minimum)!;
        Assert.Equal(2, terms.Count);
        Assert.All(terms, term => Assert.Equal(Minimum, term.Compensation));

        Assert.Null(RookieScaleRules.None.TermsFor(1, 1, Season, Cap, Minimum));
        Assert.True(RookieScaleRules.Create(BandedScale.Create([new ScaleBand(0, 9_000)]).Value, 4, 2, 2).IsFailure);
        Assert.True(RookieScaleRules.Create(BandedScale.Create([new ScaleBand(0, 800)]).Value, 4, 4, 2).IsFailure);
    }

    [Fact]
    public void ATeamFarOverTheCapStillSignsItsOwnFirstRoundPick_ButNotIntoAFullRoster()
    {
        var (team, ledger) = Team(rosterSize: 4, maximum: 6);
        var pick = Player.Create(new PlayerId("PICK"), "First Pick", Position.PointGuard, new PlayerRating(60), new DateOnly(2012, 1, 1), 0).Value;

        var signed = RookieScaleExecutor.Execute(team, pick, 1, 1, Season, Scale(), Cap, Minimum, [], ledger);

        Assert.True(signed.IsSuccess, string.Join("; ", signed.Errors.Select(error => error.Message)));
        Assert.Contains(pick.Id, team.PlayerIds);

        var (full, fullLedger) = Team(rosterSize: 6, maximum: 6);
        Assert.True(RookieScaleExecutor.Execute(full, pick, 1, 2, Season, Scale(), Cap, Minimum, [], fullLedger).IsFailure);
    }

    [Fact]
    public void ATeamTakesUpItsOptionOnARotationPlayerAndDeclinesItOnAFringeOne()
    {
        var roster = Enumerable.Range(0, 9)
            .Select(index => Player.Create(new PlayerId($"P{index}"), $"Player {index}", Position.SmallForward, new PlayerRating(80 - (index * 3)), new DateOnly(2000, 1, 1), 5).Value)
            .ToList();
        var franchise = Franchise.Create(new FranchiseId("F"), "F").Value;
        var team = Domain.Teams.Team.Create(new TeamId("T"), franchise.Id, "Testfield", new RosterSizeLimits(5, 15), roster.Select(player => player.Id)).Value;
        Contract Rookie(Player player) => Contract.Create(new ContractId($"C-{player.Id.Value}"), team.Id, player.Id, Scale().TermsFor(1, 5, new Season(2029), Cap, Minimum)!).Value;
        var star = Rookie(roster[0]);
        var fringe = Rookie(roster[8]);

        var decisions = OffseasonModel.OptionDecisions(team, roster, [star, fringe], Season, SigningTestLeague.StandardRules(), SigningTestLeague.StandardThresholds, new DateOnly(2032, 7, 1));

        Assert.True(decisions.Single(decision => decision.ContractId == star.Id).Exercise);
        var declined = decisions.Single(decision => decision.ContractId == fringe.Id);
        Assert.False(declined.Exercise);

        Assert.True(fringe.DeclineOption(declined.Season).IsSuccess);
        Assert.Null(fringe.TermFor(declined.Season));
        Assert.Equal(Season, fringe.LastSeason);
    }

    private static (Team Team, TransactionLedger Ledger) Team(int rosterSize, int maximum)
    {
        var franchise = Franchise.Create(new FranchiseId("F"), "F").Value;
        var team = Domain.Teams.Team.Create(new TeamId("T"), franchise.Id, "Testfield", new RosterSizeLimits(3, maximum),
            Enumerable.Range(0, rosterSize).Select(index => new PlayerId($"R{index}"))).Value;
        return (team, new TransactionLedger(new SteppingTestClock(new DateTimeOffset(2031, 7, 1, 9, 0, 0, TimeSpan.Zero), TimeSpan.FromMinutes(1))));
    }
}
