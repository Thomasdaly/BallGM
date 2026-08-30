using BallGM.Application.Leagues;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Players;
using BallGM.Domain.Seasons;
using BallGM.Infrastructure.Fixtures;
using BallGM.Infrastructure.Saves;
using BallGM.Infrastructure.Seasons;

namespace BallGM.Integration.Tests;

/// <summary>
/// The composed save round trip: a whole loaded league — teams, rosters, players, contracts, draft
/// assets, and the ledger — surviving a save and load intact, plus the season and negotiation state
/// a session can hold alongside it. What each embedded concept's own save format guarantees is
/// already covered by its own serializer's tests; these are about the composition and about what a
/// bad save file is refused for.
/// </summary>
public sealed class SaveGameSerializerTests
{
    private readonly SaveGameSerializer _serializer = new();

    [Fact]
    public void ALoadedLeagueSurvivesASaveAndLoadRoundTripIntact()
    {
        var snapshot = LoadFixture();

        var saved = _serializer.Save(snapshot, season: null, negotiations: new Dictionary<string, Negotiation>());
        Assert.True(saved.IsSuccess, string.Join("; ", saved.Errors.Select(error => error.Message)));

        var loaded = _serializer.Load(saved.Value);
        Assert.True(loaded.IsSuccess, string.Join("; ", loaded.Errors.Select(error => error.Message)));

        var restored = loaded.Value.Snapshot;

        Assert.Equal(snapshot.League.Id, restored.League.Id);
        Assert.Equal(snapshot.League.Name, restored.League.Name);
        Assert.Equal(snapshot.League.Alignment.Conferences.Select(c => c.Name), restored.League.Alignment.Conferences.Select(c => c.Name));
        Assert.Equal(snapshot.CurrentSeason.Year, restored.CurrentSeason.Year);
        Assert.Equal(snapshot.Franchises.Count, restored.Franchises.Count);
        Assert.Equal(snapshot.Teams.Count, restored.Teams.Count);
        Assert.Equal(snapshot.Players.Count, restored.Players.Count);
        Assert.Equal(snapshot.Contracts.Count, restored.Contracts.Count);
        Assert.Equal(snapshot.DraftAssets.Picks.Count, restored.DraftAssets.Picks.Count);
        Assert.Equal(snapshot.Ledger.Count, restored.Ledger.Count);
        Assert.Equal(snapshot.Configuration.SoftCap, restored.Configuration.SoftCap);

        // Roster membership, not just a count that could hide a shuffled team.
        foreach (var team in snapshot.Teams)
        {
            var restoredTeam = restored.Teams.Single(candidate => candidate.Id == team.Id);
            Assert.Equal(
                team.PlayerIds.Select(id => id.Value).OrderBy(value => value, StringComparer.Ordinal),
                restoredTeam.PlayerIds.Select(id => id.Value).OrderBy(value => value, StringComparer.Ordinal));
        }

        Assert.Null(loaded.Value.Season);
        Assert.Empty(loaded.Value.Negotiations);
    }

    [Fact]
    public void ASeasonInProgressAndAnOpenNegotiationSurviveTheRoundTrip()
    {
        var snapshot = LoadFixture();

        var seasonEngine = new RulesSeasonEngine();
        var started = seasonEngine.Start(snapshot, new DateOnly(snapshot.CurrentSeason.Year, 7, 1), seed: 11);
        Assert.True(started.IsSuccess, string.Join("; ", started.Errors.Select(error => error.Message)));
        Assert.True(started.Value.Run.AdvanceTo(new SeasonDay(5)).IsSuccess);

        var freeAgent = snapshot.Players.First(player => !snapshot.Teams.Any(team => team.PlayerIds.Contains(player.Id)));
        var negotiationResult = Negotiation.Open(new NegotiationId("NEGOTIATION-1"), freeAgent.Id, SeasonDay.Opening);
        Assert.True(negotiationResult.IsSuccess);
        var negotiations = new Dictionary<string, Negotiation> { [negotiationResult.Value.PlayerId.Value] = negotiationResult.Value };

        var saved = _serializer.Save(snapshot, started.Value.Run, negotiations);
        Assert.True(saved.IsSuccess, string.Join("; ", saved.Errors.Select(error => error.Message)));

        var loaded = _serializer.Load(saved.Value);
        Assert.True(loaded.IsSuccess, string.Join("; ", loaded.Errors.Select(error => error.Message)));

        Assert.NotNull(loaded.Value.Season);
        Assert.Equal(started.Value.Run.Season.Year, loaded.Value.Season!.Season.Year);
        Assert.Equal(started.Value.Run.Seed.Value, loaded.Value.Season!.Seed.Value);
        Assert.Equal(started.Value.Run.CurrentDay, loaded.Value.Season!.CurrentDay);

        var restoredNegotiation = Assert.Single(loaded.Value.Negotiations.Values);
        Assert.Equal(freeAgent.Id, restoredNegotiation.PlayerId);
    }

    [Fact]
    public void SaveDeclaresItsSchemaVersion()
    {
        var saved = _serializer.Save(LoadFixture(), null, new Dictionary<string, Negotiation>());

        Assert.Contains("\"schemaVersion\": 1", saved.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void SaveFromAFutureSchema_ExplainsItselfInsteadOfLoadingHalfOfIt()
    {
        var saved = _serializer.Save(LoadFixture(), null, new Dictionary<string, Negotiation>());
        var mutated = saved.Value.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 99", StringComparison.Ordinal);

        var result = _serializer.Load(mutated);

        Assert.True(result.IsFailure);
        Assert.Equal("save_game.unsupported_schema_version", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void MalformedSaveJson_ExplainsItselfInsteadOfThrowing()
    {
        var result = _serializer.Load("{ this is not json");

        Assert.True(result.IsFailure);
        Assert.Equal("save_game.malformed_file", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void SaveWithAnUnknownField_ExplainsItselfInsteadOfSilentlyDroppingHalfOfIt()
    {
        var saved = _serializer.Save(LoadFixture(), null, new Dictionary<string, Negotiation>());
        var firstBrace = saved.Value.IndexOf('{');
        var mutated = saved.Value.Insert(firstBrace + 1, "\n  \"aFieldThisBuildHasNeverHeardOf\": true,");

        var result = _serializer.Load(mutated);

        Assert.True(result.IsFailure);
        Assert.Equal("save_game.malformed_file", Assert.Single(result.Errors).Code);
    }

    private static LeagueSnapshot LoadFixture()
    {
        var result = new FixtureLeagueDataSource().Load();
        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));
        return result.Value;
    }
}
