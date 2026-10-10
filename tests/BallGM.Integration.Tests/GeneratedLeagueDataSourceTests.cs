using BallGM.Infrastructure.Fixtures;
using BallGM.Infrastructure.LeaguePacks;

namespace BallGM.Integration.Tests;

/// <summary>The seeded thirty-team league the league-shape calibration measures against.</summary>
public sealed class GeneratedLeagueDataSourceTests
{
    [Fact]
    public void TheCalibrationLeagueIsThirtyFullRostersPlayingAnEightyTwoGameSeason()
    {
        var loaded = new GeneratedLeagueDataSource(GeneratedLeagueDataSource.CalibrationRulesetFilePath, seed: 7).Load();

        Assert.True(loaded.IsSuccess, string.Join("; ", loaded.Errors.Select(error => error.Message)));
        var league = loaded.Value;
        Assert.Equal(30, league.Teams.Count);
        Assert.All(league.Teams, team => Assert.Equal(15, team.PlayerIds.Count));
        Assert.Equal(82, league.Configuration.RegularSeasonGameCount);
        Assert.Equal(2, league.League.Alignment.Conferences.Count);
        Assert.All(league.League.Alignment.Conferences, conference => Assert.Equal(3, conference.Divisions.Count));
    }

    [Fact]
    public void TheSameSeedGeneratesTheSameLeagueAndAnotherSeedAnotherOne()
    {
        var first = GeneratedLeagueDataSource.Envelope(seed: 11, teamCount: 30);
        var again = GeneratedLeagueDataSource.Envelope(seed: 11, teamCount: 30);
        var other = GeneratedLeagueDataSource.Envelope(seed: 12, teamCount: 30);

        Assert.Equal(first.Players!.Select(player => (player.Name, player.Ratings)), again.Players!.Select(player => (player.Name, player.Ratings)));
        Assert.NotEqual(first.Players!.Select(player => player.Ratings), other.Players!.Select(player => player.Ratings));
    }

    [Fact]
    public void ALeagueOfAnyMultipleOfSixTeamsIsAccepted_AndOtherSizesAreNot()
    {
        Assert.Equal(12, GeneratedLeagueDataSource.Envelope(seed: 1, teamCount: 12).Teams!.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => new GeneratedLeagueDataSource(GeneratedLeagueDataSource.CalibrationRulesetFilePath, 1, teamCount: 28));
    }

    [Fact]
    public void PackContentBuiltInMemoryIsValidatedLikeAPackOnDisk()
    {
        var envelope = GeneratedLeagueDataSource.Envelope(seed: 3, teamCount: 6) with { SchemaVersion = 99 };
        var ruleset = new BallGM.Infrastructure.Rulesets.LeagueRulesetSerializer()
            .Deserialize(File.ReadAllText(GeneratedLeagueDataSource.CalibrationRulesetFilePath)).Value;

        Assert.Equal("league_pack.unsupported_schema_version", Assert.Single(LeaguePackDataSource.FromEnvelope(envelope, ruleset).Errors).Code);
    }
}
