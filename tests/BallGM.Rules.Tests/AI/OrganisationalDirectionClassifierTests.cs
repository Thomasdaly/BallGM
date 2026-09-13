using BallGM.Domain.AI;
using BallGM.Domain.Leagues;
using BallGM.Domain.Players;
using BallGM.Domain.Seasons;
using BallGM.Domain.Teams;
using BallGM.Rules.AI;
using BallGM.Rules.Configuration;

namespace BallGM.Rules.Tests.AI;

public sealed class OrganisationalDirectionClassifierTests
{
    private static readonly Season CurrentSeason = new(2031);
    private static readonly TeamId TeamId = new("TEAM-A");

    private static readonly DevelopmentRules PeakRules =
        DevelopmentRules.Create(peakAgeStart: 24, peakAgeEnd: 29, growthCurve: null, declineCurve: null, varianceRange: 0).Value;

    [Fact]
    public void WinningRecordClassifiesAsContendingRegardlessOfAge()
    {
        var roster = Roster(age: 34, count: 12);
        var standing = Standing(wins: 45, losses: 20);

        var result = OrganisationalDirectionClassifier.Classify(TeamId, CurrentSeason, roster, standing, PeakRules);

        Assert.Equal(CompetitiveDirection.Contending, result.Direction);
        Assert.Contains(result.Factors, finding => finding.RuleCode == "ai_direction.win_percent");
    }

    [Fact]
    public void LosingRecordWithYoungRosterClassifiesAsRetooling()
    {
        var roster = Roster(age: 21, count: 12);
        var standing = Standing(wins: 20, losses: 45);

        var result = OrganisationalDirectionClassifier.Classify(TeamId, CurrentSeason, roster, standing, PeakRules);

        Assert.Equal(CompetitiveDirection.Retooling, result.Direction);
        Assert.Contains(result.Factors, finding => finding.RuleCode == "ai_direction.young_core");
    }

    [Fact]
    public void LosingRecordWithVeteranRosterClassifiesAsRebuilding()
    {
        var roster = Roster(age: 34, count: 12);
        var standing = Standing(wins: 20, losses: 45);

        var result = OrganisationalDirectionClassifier.Classify(TeamId, CurrentSeason, roster, standing, PeakRules);

        Assert.Equal(CompetitiveDirection.Rebuilding, result.Direction);
        Assert.Contains(result.Factors, finding => finding.RuleCode == "ai_direction.veteran_core");
    }

    [Fact]
    public void NoStandingsYetFallsBackToRosterOverall()
    {
        var strongRoster = Roster(age: 27, count: 12, overall: 70);

        var result = OrganisationalDirectionClassifier.Classify(TeamId, CurrentSeason, strongRoster, standing: null, PeakRules);

        Assert.Equal(CompetitiveDirection.Contending, result.Direction);
        Assert.Contains(result.Factors, finding => finding.RuleCode == "ai_direction.no_standings_yet");
    }

    [Fact]
    public void StandingWithNoGamesPlayedIsTreatedAsNoStandings()
    {
        var strongRoster = Roster(age: 27, count: 12, overall: 70);
        var emptyStanding = Standing(wins: 0, losses: 0);

        var result = OrganisationalDirectionClassifier.Classify(TeamId, CurrentSeason, strongRoster, emptyStanding, PeakRules);

        Assert.Contains(result.Factors, finding => finding.RuleCode == "ai_direction.no_standings_yet");
    }

    [Fact]
    public void UnconfiguredDevelopmentRulesFallBackToTheDefaultPeakAge()
    {
        var roster = Roster(age: 21, count: 12);
        var standing = Standing(wins: 20, losses: 45);

        var result = OrganisationalDirectionClassifier.Classify(TeamId, CurrentSeason, roster, standing, DevelopmentRules.None);

        Assert.Equal(CompetitiveDirection.Retooling, result.Direction);
    }

    [Fact]
    public void EmptyRosterDoesNotThrow()
    {
        var result = OrganisationalDirectionClassifier.Classify(TeamId, CurrentSeason, [], standing: null, PeakRules);

        Assert.Equal(CompetitiveDirection.Rebuilding, result.Direction);
    }

    private static StandingsRow Standing(int wins, int losses) => new(
        TeamId,
        "Club",
        null,
        null,
        new TeamRecord(wins, losses),
        null,
        null,
        wins * 100,
        losses * 100);

    private static List<Player> Roster(int age, int count, int overall = 50)
    {
        var referenceYear = CurrentSeason.Year;
        var players = new List<Player>();

        for (var index = 0; index < count; index++)
        {
            players.Add(Player.Create(
                new PlayerId($"PLAYER-{index}"),
                $"Player {index}",
                Position.SmallForward,
                new PlayerRating(overall),
                new DateOnly(referenceYear - age, 1, 1),
                seasonsOfService: Math.Max(0, age - 19)).Value);
        }

        return players;
    }
}
