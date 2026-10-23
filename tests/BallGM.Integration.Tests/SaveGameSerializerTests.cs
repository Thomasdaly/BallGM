using System.Text.Json.Nodes;
using BallGM.Application.Leagues;
using BallGM.Application.Players;
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
    public void ASavedPlayerKeepsHisWholeSheet_HisRetirementAndHisBiography()
    {
        // Every version before 5 dropped retirement and biography on every save (regression).
        var snapshot = LoadFixture();
        var original = snapshot.Players.First();
        var retired = Player.Create(
            original.Id,
            original.FullName,
            original.Position,
            original.Rating.With(HiddenAttribute.Loyalty, 93),
            original.BirthDate,
            original.SeasonsOfService,
            biography: new PlayerBiography("Harrowgate", "Northcliff State", 2027, 1, 4),
            isRetired: true).Value;
        snapshot = snapshot with { Players = [retired, .. snapshot.Players.Skip(1)] };

        var saved = _serializer.Save(snapshot, null, new Dictionary<string, Negotiation>());
        var loaded = _serializer.Load(saved.Value);

        Assert.True(loaded.IsSuccess, string.Join("; ", loaded.Errors.Select(error => error.Message)));
        var back = loaded.Value.Snapshot.Players.Single(player => player.Id == original.Id);
        Assert.Equal(retired.Rating, back.Rating);
        Assert.True(back.IsRetired);
        Assert.Equal(retired.Biography, back.Biography);
        Assert.All(snapshot.Players, player => Assert.Equal(player.Rating, loaded.Value.Snapshot.Players.Single(other => other.Id == player.Id).Rating));
    }

    [Fact]
    public void VersionFourSave_IsRefusedBecauseItsPlayersCarryOnlyFiveAttributes()
    {
        var saved = _serializer.Save(LoadFixture(), null, new Dictionary<string, Negotiation>());

        var result = _serializer.Load(saved.Value.Replace($"\"schemaVersion\": {SaveGameEnvelope.CurrentSchemaVersion}", "\"schemaVersion\": 4", StringComparison.Ordinal));

        Assert.Equal("save_game.unsupported_schema_version", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void SaveDeclaresItsSchemaVersion()
    {
        var saved = _serializer.Save(LoadFixture(), null, new Dictionary<string, Negotiation>());

        Assert.Contains("\"schemaVersion\": 8", saved.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void SaveFromAFutureSchema_ExplainsItselfInsteadOfLoadingHalfOfIt()
    {
        var saved = _serializer.Save(LoadFixture(), null, new Dictionary<string, Negotiation>());
        var mutated = saved.Value.Replace($"\"schemaVersion\": {SaveGameEnvelope.CurrentSchemaVersion}", "\"schemaVersion\": 99", StringComparison.Ordinal);

        var result = _serializer.Load(mutated);

        Assert.True(result.IsFailure);
        Assert.Equal("save_game.unsupported_schema_version", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void VersionThreeSave_IsRefusedBecauseItsCareerUsageMeansSomethingElse()
    {
        // Version 3's concluded seasons hold USG_PCT as the engine's share of team plays; version 4's
        // hold the minutes-normalised estimate. Loading one would mix the two in one career column.
        var saved = _serializer.Save(LoadFixture(), null, new Dictionary<string, Negotiation>());
        var versionThree = saved.Value.Replace($"\"schemaVersion\": {SaveGameEnvelope.CurrentSchemaVersion}", "\"schemaVersion\": 3", StringComparison.Ordinal);

        var result = _serializer.Load(versionThree);

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

    [Fact]
    public void SaveFromBeforeCareerHistoryWasSaved_IsRefusedRatherThanLoadedShort()
    {
        var saved = _serializer.Save(LoadFixture(), null, new Dictionary<string, Negotiation>());
        var node = JsonNode.Parse(saved.Value)!.AsObject();
        node["schemaVersion"] = 2;
        node.Remove("careerHistory");
        node.Remove("taxRepeaterTeams");

        var result = _serializer.Load(node.ToJsonString());

        Assert.Equal("save_game.unsupported_schema_version", Assert.Single(result.Errors).Code);
    }

    [Theory]
    [InlineData("missing career list")]
    [InlineData("career for nobody in the save")]
    [InlineData("career named twice")]
    [InlineData("career season missing a total")]
    [InlineData("career season with a negative total")]
    [InlineData("career season with a blank label")]
    [InlineData("missing repeater list")]
    [InlineData("repeater for no team in the save")]
    [InlineData("repeater named twice")]
    public void SaveWithUnreadableCareerOrRepeaterContent_IsRefusedWithAStructuredError(string defect)
    {
        var snapshot = LoadFixture();
        var playerId = snapshot.Players.First().Id.Value;
        var teamId = snapshot.Teams.First().Id.Value;
        snapshot = snapshot with
        {
            CareerHistory = new Dictionary<string, IReadOnlyList<CareerSeasonLine>>
            {
                [playerId] = [new CareerSeasonLine("2029-30", "Old Club", 70, 2100, 1400, 500, 300)],
            },
            TaxRepeaterTeams = new HashSet<string> { teamId },
        };
        var node = JsonNode.Parse(_serializer.Save(snapshot, null, new Dictionary<string, Negotiation>()).Value)!.AsObject();
        var career = node["careerHistory"]!.AsArray();
        var season = career[0]!["seasons"]![0]!.AsObject();

        switch (defect)
        {
            case "missing career list": node.Remove("careerHistory"); break;
            case "career for nobody in the save": career[0]!["playerId"] = "NOBODY"; break;
            case "career named twice": career.Add(career[0]!.DeepClone()); break;
            case "career season missing a total": season.Remove("points"); break;
            case "career season with a negative total": season["minutes"] = -1; break;
            case "career season with a blank label": season["season"] = " "; break;
            case "missing repeater list": node.Remove("taxRepeaterTeams"); break;
            case "repeater for no team in the save": node["taxRepeaterTeams"] = new JsonArray("NOT-A-TEAM"); break;
            case "repeater named twice": node["taxRepeaterTeams"] = new JsonArray(teamId, teamId); break;
        }

        var result = _serializer.Load(node.ToJsonString());

        Assert.True(result.IsFailure, defect);
        Assert.Equal("save_game.invalid_field", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void EveryLeagueSnapshotPropertyIsSavedOrDeliberatelyNot()
    {
        // The career-history loss happened because LeagueSnapshot's init properties are optional, so
        // nothing forced the loader to supply them. A new one must be added to one of these lists.
        string[] saved = [nameof(LeagueSnapshot.CareerHistory), nameof(LeagueSnapshot.TaxRepeaterTeams)];
        string[] deliberatelyNotSaved = [nameof(LeagueSnapshot.Artwork)];
        var positional = typeof(LeagueSnapshot).GetConstructors().Single().GetParameters().Select(parameter => parameter.Name!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var initOnly = typeof(LeagueSnapshot).GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
            .Where(property => property.Name != "EqualityContract" && !positional.Contains(property.Name))
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal);

        Assert.Equal(saved.Concat(deliberatelyNotSaved).Order(StringComparer.Ordinal), initOnly);
    }

    private static LeagueSnapshot LoadFixture()
    {
        var result = new FixtureLeagueDataSource().Load();
        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));
        return result.Value;
    }
}
