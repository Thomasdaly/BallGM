using BallGM.Domain.Common;
using BallGM.Domain.Leagues;
using BallGM.Domain.Players;
using BallGM.Domain.Seasons;
using BallGM.Domain.Teams;
using BallGM.Domain.Trades;
using BallGM.Rules.Configuration;
using BallGM.Rules.Seasons;
using BallGM.Simulation.Seasons;

namespace BallGM.Simulation.Tests;

/// <summary>
/// A match engine with no probability in it: the team whose identifier sorts first always wins, by
/// the same margin every time.
/// <para>
/// Deliberately not a model of basketball. A postseason test is about the bracket — who meets whom,
/// where, and when the season ends — and a seeded probabilistic engine would make every one of those
/// assertions a statement about a random stream instead. Here the table's order is known before a
/// game is played, so the bracket the rules draw from it is checkable by hand.
/// </para>
/// </summary>
internal sealed class OrdinalMatchEngine : IMatchEngine
{
    public bool CanPlay => true;

    public int GamesPlayed { get; private set; }

    public DomainOperationResult<GameResult> Play(MatchSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);

        GamesPlayed++;

        var homeWins = string.CompareOrdinal(
            setup.Fixture.HomeTeamId.Value,
            setup.Fixture.AwayTeamId.Value) < 0;

        return GameResult.Create(setup.Fixture, homeWins ? 101 : 99, homeWins ? 99 : 101);
    }
}

/// <summary>Builds the leagues and rulesets the season engine tests are run against.</summary>
internal static class SeasonTestFixtures
{
    public static readonly Season Season = new(2031);

    public static readonly DateOnly Opening = new(2031, 7, 1);

    public static TeamId TeamAt(int index) => new($"TEAM-{index:D2}");

    public static League Flat(int teamCount)
    {
        var result = League.Create(
            new LeagueId("LEAGUE-TEST"),
            "Test League",
            Enumerable.Range(0, teamCount).Select(TeamAt));

        Assert.True(result.IsSuccess);
        return result.Value;
    }

    public static SeasonContext Context(League league, PostseasonRules postseason, int regularSeasonDays = 10)
    {
        var scheduleRules = ScheduleRules.Create(0, regularSeasonDays, 0);
        Assert.True(scheduleRules.IsSuccess);

        var standingsRules = StandingsRules.Create([StandingsTieBreak.PointDifferential]);
        Assert.True(standingsRules.IsSuccess);

        var tradeRules = TradeRules.Create(null, null, InjuredPlayerTradeEligibility.Allowed, false);
        Assert.True(tradeRules.IsSuccess);

        var ruleset = new LeagueRuleset(
            LeagueRuleset.CurrentSchemaVersion,
            "Test Ruleset",
            league.TeamIds.Count - 1,
            new RosterSizeLimits(5, 15),
            CapThresholds.Uncapped,
            DraftRules.NoDraft,
            tradeRules.Value,
            NegotiationRules.OpenMarket,
            scheduleRules.Value,
            standingsRules.Value,
            postseason);

        var teams = league.TeamIds
            .OrderBy(teamId => teamId.Value, StringComparer.Ordinal)
            .Select(teamId => new SeasonTeam(teamId, $"Club {teamId.Value}", 8, Squad(teamId)))
            .ToList();

        return new SeasonContext(Season, league, ruleset, teams);
    }

    public static PostseasonRules Postseason(
        int qualifiers,
        IReadOnlyList<int> seriesLengths,
        int postseasonDays,
        int? eligibilityCutoffDay = null,
        int regularSeasonEndDay = 10,
        bool includesFinal = false)
    {
        var created = PostseasonRules.Create(
            postseasonDays,
            qualifiers,
            seriesLengths,
            "2-2-1-1-1",
            eligibilityCutoffDay,
            regularSeasonEndDay,
            includesFinal);

        Assert.True(created.IsSuccess);
        return created.Value;
    }

    private static IReadOnlyList<AvailablePlayer> Squad(TeamId teamId)
    {
        var positions = Enum.GetValues<Position>();

        return Enumerable.Range(0, 8)
            .Select(index => new AvailablePlayer(
                new PlayerId($"{teamId.Value}-P{index:D2}"),
                positions[index % positions.Length],
                80 - index))
            .ToList();
    }
}
