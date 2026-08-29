using BallGM.Domain.Common;
using BallGM.Domain.Leagues;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Players;
using BallGM.Domain.Seasons;
using BallGM.Domain.Teams;
using BallGM.Rules.Configuration;
using BallGM.Rules.Seasons;

namespace BallGM.Rules.Tests;

/// <summary>
/// Builds small leagues for the schedule, standings, and rotation tests: a stated number of teams,
/// optionally aligned into conferences, with identifiers that sort in the order they were created so
/// an assertion about deterministic ordering has something stable to assert against.
/// </summary>
internal static class SeasonTestLeague
{
    public static readonly Season Season = new(2031);

    public static readonly DateOnly Opening = new(2031, 7, 1);

    public static TeamId TeamAt(int index) => new($"TEAM-{index:D2}");

    /// <summary>A flat league of <paramref name="teamCount"/> teams.</summary>
    public static League Flat(int teamCount)
    {
        var result = League.Create(
            new LeagueId("LEAGUE-TEST"),
            "Test League",
            Enumerable.Range(0, teamCount).Select(TeamAt));

        Assert.True(result.IsSuccess);
        return result.Value;
    }

    /// <summary>Two conferences of one division each, split down the middle.</summary>
    public static League TwoConferences(int teamCount)
    {
        var teams = Enumerable.Range(0, teamCount).Select(TeamAt).ToArray();
        var half = (teamCount + 1) / 2;

        var alignment = LeagueAlignment.Create(
        [
            new LeagueConference("Coastal", [new LeagueDivision("Tidewater", teams.Take(half))]),
            new LeagueConference("Interior", [new LeagueDivision("Highland", teams.Skip(half))]),
        ]);

        Assert.True(alignment.IsSuccess);

        var result = League.Create(new LeagueId("LEAGUE-TEST"), "Test League", teams, alignment.Value);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    public static IReadOnlyDictionary<TeamId, string> Names(League league) =>
        league.TeamIds.ToDictionary(teamId => teamId, teamId => $"Club {teamId.Value}");

    public static LeagueCalendar Calendar(int regularSeasonDays, int preseasonDays = 0, int postseasonDays = 0)
    {
        var scheduleRules = ScheduleRules.Create(preseasonDays, regularSeasonDays, 0);
        Assert.True(scheduleRules.IsSuccess);

        var postseason = PostseasonRules.None;
        if (postseasonDays > 0)
        {
            var created = PostseasonRules.Create(postseasonDays, 2, [5, 7], "2-2-1-1-1", null, preseasonDays + regularSeasonDays);
            Assert.True(created.IsSuccess);
            postseason = created.Value;
        }

        var calendar = new SeasonCalendarBuilder().Build(Season, Opening, scheduleRules.Value, postseason);
        Assert.True(calendar.IsSuccess);
        return calendar.Value;
    }

    /// <summary>A squad covering all five positions, plus bench, with descending ratings.</summary>
    public static IReadOnlyList<AvailablePlayer> Squad(TeamId teamId, int size, int topRating = 80)
    {
        var positions = Enum.GetValues<Position>();

        return Enumerable.Range(0, size)
            .Select(index => new AvailablePlayer(
                new PlayerId($"{teamId.Value}-P{index:D2}"),
                positions[index % positions.Length],
                Math.Max(40, topRating - index)))
            .ToList();
    }

    public static GameResult Result(Season season, SeasonDay day, int slot, TeamId home, TeamId away, int homePoints, int awayPoints)
    {
        var fixture = new Fixture(GameId.For(season, day, slot), day, home, away, SeasonPhase.RegularSeason);
        var result = GameResult.Create(fixture, homePoints, awayPoints);
        Assert.True(result.IsSuccess);
        return result.Value;
    }
}
