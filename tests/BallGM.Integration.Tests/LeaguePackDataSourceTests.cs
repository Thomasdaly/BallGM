using System.Text.Json.Nodes;
using BallGM.Application.Leagues;
using BallGM.Infrastructure.AI;
using BallGM.Infrastructure.Cap;
using BallGM.Infrastructure.DraftAssets;
using BallGM.Infrastructure.Fixtures;
using BallGM.Infrastructure.LeaguePacks;
using BallGM.Infrastructure.Negotiations;
using BallGM.Infrastructure.Saves;
using BallGM.Infrastructure.Seasons;
using BallGM.Infrastructure.Trades;

namespace BallGM.Integration.Tests;

/// <summary>
/// A league pack on disk, through <see cref="LeaguePackDataSource"/>, into a league that loads, plays,
/// and concludes a season — and every way a pack's content can be wrong reported as a structured
/// failure rather than a thrown exception.
/// </summary>
public sealed class LeaguePackDataSourceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ballgm-pack-" + Guid.NewGuid().ToString("N"));

    public LeaguePackDataSourceTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Pack_LoadsTeamsRostersContractsAndFreeAgents()
    {
        var result = Load(SamplePack());

        Assert.True(result.IsSuccess, Describe(result));
        var snapshot = result.Value;

        Assert.Equal("Sample Pack League", snapshot.League.Name);
        Assert.Equal(2040, snapshot.CurrentSeason.Year);
        Assert.Equal(4, snapshot.Teams.Count);
        Assert.All(snapshot.Teams, team => Assert.Equal(12, team.RosterCount));
        Assert.Equal(49, snapshot.Players.Count);
        Assert.Equal(48, snapshot.Contracts.Count);
        Assert.Equal(48, snapshot.Ledger.Entries.Count);
        Assert.False(snapshot.League.Alignment.IsFlat);
    }

    [Fact]
    public void Pack_StoresTheFiveAttributesItStates()
    {
        var snapshot = Load(SamplePack()).Value;

        var star = snapshot.Players.Single(player => player.FullName == "Player T0-S0");
        Assert.Equal(90, star.Rating.Height);
        Assert.Equal(70, star.Rating.Speed);
        Assert.Equal(85, star.Rating.Strength);
        Assert.Equal(60, star.Rating.Passing);
        Assert.Equal(65, star.Rating.LateralQuickness);
    }

    [Fact]
    public void Pack_FinalSeasonOptionIsNotGuaranteedMoney()
    {
        var pack = SamplePack();
        pack["players"]![0]!["contract"]!["finalSeasonOption"] = "Team";

        var snapshot = Load(pack).Value;
        var player = snapshot.Players.Single(candidate => candidate.FullName == "Player T0-S0");
        var contract = snapshot.Contracts.Single(candidate => candidate.PlayerId == player.Id);

        Assert.True(contract.Terms[^1].IsPendingOption);
        Assert.Equal(0, contract.Terms[^1].GuaranteedAmount.SmallestUnits);
    }

    [Fact]
    public void Pack_RegistersEveryFranchisesOwnPicksWithNoHistory()
    {
        var snapshot = Load(SamplePack()).Value;

        Assert.NotEmpty(snapshot.DraftAssets.Picks);
        Assert.All(snapshot.DraftAssets.Picks, pick => Assert.Equal(pick.OriginalFranchiseId, snapshot.DraftAssets.Ownership(pick.Id)!.CurrentOwnerFranchiseId));
    }

    [Fact]
    public void Pack_LeaguePlaysAndConcludesASeason()
    {
        var path = Write(SamplePack());
        var session = new LeagueSession(
            new LeaguePackDataSource(path),
            new RulesCapLedger(),
            new RulesDraftAssetLedger(),
            new RulesTradeEngine(),
            new RulesSigningEngine(),
            new RulesFreeAgencyMarket(),
            new RulesSeasonEngine(),
            new SaveGameSerializer(),
            new RulesFrontOfficeAdvisor(new RulesCapLedger()));

        Assert.True(session.Load().IsSuccess);
        Assert.True(session.StartSeason(seed: 11).IsSuccess);
        Assert.True(session.AdvanceToEndOfSeason().IsSuccess);

        var conclusion = session.ConcludeSeason();
        Assert.True(conclusion.IsSuccess, string.Join("; ", conclusion.Errors.Select(error => error.Message)));
    }

    [Fact]
    public void Pack_RulesetFileIsReadRelativeToThePack()
    {
        File.Copy(FixtureLeagueDataSource.DefaultRulesetFilePath, Path.Combine(_directory, "my-rules.json"));
        var pack = SamplePack();
        pack["rulesetFile"] = "my-rules.json";

        Assert.True(Load(pack).IsSuccess);

        pack["rulesetFile"] = "missing-rules.json";
        AssertFails(Load(pack), "league_pack.file_missing");
    }

    [Fact]
    public void Pack_RefusesAnUnsupportedSchemaVersion()
    {
        var pack = SamplePack();
        pack["schemaVersion"] = 99;

        AssertFails(Load(pack), "league_pack.unsupported_schema_version");
    }

    [Fact]
    public void Pack_RefusesAFieldThisBuildDoesNotKnow()
    {
        var pack = SamplePack();
        pack["salaryCapOverride"] = 1;

        AssertFails(Load(pack), "league_pack.malformed_file");
    }

    [Fact]
    public void Pack_RefusesAPlayerOnATeamItDoesNotDefine()
    {
        var pack = SamplePack();
        pack["players"]![0]!["team"] = "ZZZ";

        AssertFails(Load(pack), "league_pack.unknown_team");
    }

    [Fact]
    public void Pack_RefusesARosteredPlayerWithoutAContract()
    {
        var pack = SamplePack();
        pack["players"]![0]!.AsObject().Remove("contract");

        AssertFails(Load(pack), "league_pack.contract_mismatch");
    }

    [Fact]
    public void Pack_RefusesAFreeAgentUnderContract()
    {
        var pack = SamplePack();
        pack["players"]!.AsArray()[^1]!["contract"] = new JsonObject { ["salaries"] = new JsonArray(1_000_000L) };

        AssertFails(Load(pack), "league_pack.contract_mismatch");
    }

    [Fact]
    public void Pack_RefusesAnOutOfRangeRatingAndReportsEveryProblemAtOnce()
    {
        var pack = SamplePack();
        pack["players"]![0]!["ratings"]!["height"] = 101;
        pack["players"]![1]!["position"] = "Goalkeeper";

        var result = Load(pack);

        AssertFails(result, "league_pack.invalid_field");
        Assert.True(result.Errors.Count >= 2);
    }

    [Fact]
    public void Pack_RefusesARosterOverTheRulesetLimit()
    {
        var pack = SamplePack();
        var players = pack["players"]!.AsArray();
        for (var extra = 0; extra < 4; extra++)
        {
            players.Add(PlayerNode($"Extra {extra}", "BLU", 60, withContract: true));
        }

        Assert.True(Load(pack).IsFailure);
    }

    [Fact]
    public void Pack_WithoutArtwork_LoadsWithNoImages()
    {
        var snapshot = Load(SamplePack()).Value;

        Assert.Empty(snapshot.Artwork.TeamLogos);
        Assert.Empty(snapshot.Artwork.PlayerPortraits);
    }

    [Fact]
    public void Pack_ResolvesLogosAndPortraitsInsideItsFolder()
    {
        Directory.CreateDirectory(Path.Combine(_directory, "art"));
        File.WriteAllBytes(Path.Combine(_directory, "art", "red.png"), [0x89, 0x50]);
        File.WriteAllBytes(Path.Combine(_directory, "art", "star.jpg"), [0xFF, 0xD8]);
        var pack = SamplePack();
        pack["teams"]![0]!["logo"] = "art/red.png";
        pack["players"]![0]!["portrait"] = "art/star.jpg";
        pack["players"]![1]!["portrait"] = "art/not-there.png";

        var result = Load(pack);

        Assert.True(result.IsSuccess, Describe(result));
        var snapshot = result.Value;
        var red = snapshot.Teams.Single(team => team.Name == "RED Team");
        var star = snapshot.Players.Single(player => player.FullName == "Player T0-S0");
        Assert.Equal(Path.Combine(_directory, "art", "red.png"), snapshot.Artwork.LogoFor(red.Id));
        Assert.Equal(Path.Combine(_directory, "art", "star.jpg"), snapshot.Artwork.PortraitFor(star.Id));
        Assert.Single(snapshot.Artwork.PlayerPortraits);
    }

    [Theory]
    [InlineData("../outside.png")]
    [InlineData("/etc/passwd.png")]
    [InlineData("art/notes.txt")]
    public void Pack_RefusesAnImagePathOutsideItsFolderOrNotAnImage(string path)
    {
        var pack = SamplePack();
        pack["players"]![0]!["portrait"] = path;

        AssertFails(Load(pack), "league_pack.invalid_image_path");
    }

    [Fact]
    public void Pack_TradedPickIsControlledByItsNewOwnerAndLedgered()
    {
        var pack = SamplePack();
        pack["pickTrades"] = new JsonArray(new JsonObject { ["draft"] = 2041, ["round"] = 1, ["original"] = "RED", ["owner"] = "BLU", ["note"] = "from the big trade" });

        var result = Load(pack);

        Assert.True(result.IsSuccess, Describe(result));
        var snapshot = result.Value;
        var red = snapshot.Franchises.Single(franchise => franchise.Name == "RED Club");
        var blu = snapshot.Franchises.Single(franchise => franchise.Name == "BLU Club");
        var pick = snapshot.DraftAssets.Find(new BallGM.Domain.Leagues.Season(2041), 1, red.Id)!;
        Assert.Equal(blu.Id, snapshot.DraftAssets.Ownership(pick.Id)!.CurrentOwnerFranchiseId);
        Assert.Contains(snapshot.Ledger.Entries, entry => entry.Kind == BallGM.Domain.Transactions.TransactionKind.DraftPickTransferred);
    }

    [Fact]
    public void Pack_ProtectedObligationAndSwapRideOnTheControllersPick()
    {
        var pack = SamplePack();
        pack["pickTrades"] = new JsonArray(
            new JsonObject { ["draft"] = 2041, ["round"] = 1, ["original"] = "RED", ["owner"] = "BLU" },
            new JsonObject { ["draft"] = 2041, ["round"] = 1, ["original"] = "RED", ["owedTo"] = "GRN", ["protectedTop"] = new JsonArray(4, 4), ["fallback"] = "ConvertsToRound", ["fallbackRound"] = 2 },
            new JsonObject { ["draft"] = 2042, ["round"] = 1, ["original"] = "GLD", ["swapHolder"] = "RED" });

        var result = Load(pack);

        Assert.True(result.IsSuccess, Describe(result));
        var snapshot = result.Value;
        var red = snapshot.Franchises.Single(franchise => franchise.Name == "RED Club");
        var grn = snapshot.Franchises.Single(franchise => franchise.Name == "GRN Club");
        var gld = snapshot.Franchises.Single(franchise => franchise.Name == "GLD Club");
        var owed = snapshot.DraftAssets.Ownership(snapshot.DraftAssets.Find(new BallGM.Domain.Leagues.Season(2041), 1, red.Id)!.Id)!;
        Assert.Equal(grn.Id, owed.Obligation!.BeneficiaryFranchiseId);
        Assert.Equal([4, 4], owed.Obligation.Protection.ProtectedSelections);
        var swapped = snapshot.DraftAssets.Ownership(snapshot.DraftAssets.Find(new BallGM.Domain.Leagues.Season(2042), 1, gld.Id)!.Id)!;
        Assert.NotNull(swapped.PendingSwap);
        Assert.Equal(red.Id, swapped.PendingSwap!.HolderFranchiseId);
    }

    [Theory]
    [InlineData("""{"draft":2041,"round":1,"original":"RED","owner":"BLU","owedTo":"GRN"}""")]
    [InlineData("""{"draft":2041,"round":1,"original":"RED"}""")]
    [InlineData("""{"draft":2041,"round":1,"original":"RED","owner":"PUR"}""")]
    [InlineData("""{"draft":2099,"round":1,"original":"RED","owner":"BLU"}""")]
    [InlineData("""{"draft":2041,"round":3,"original":"RED","owner":"BLU"}""")]
    [InlineData("""{"draft":2041,"round":1,"original":"RED","owner":"BLU","protectedTop":[4]}""")]
    [InlineData("""{"draft":2041,"round":1,"original":"RED","owedTo":"BLU","fallback":"Sometimes"}""")]
    public void Pack_RefusesAMalformedPickTrade(string trade)
    {
        var pack = SamplePack();
        pack["pickTrades"] = new JsonArray(JsonNode.Parse(trade));

        Assert.True(Load(pack).IsFailure);
    }

    [Fact]
    public void Pack_RefusesAPickTradedTwice()
    {
        var pack = SamplePack();
        pack["pickTrades"] = new JsonArray(
            new JsonObject { ["draft"] = 2041, ["round"] = 1, ["original"] = "RED", ["owner"] = "BLU" },
            new JsonObject { ["draft"] = 2041, ["round"] = 1, ["original"] = "RED", ["owner"] = "GRN" });

        AssertFails(Load(pack), "league_pack.invalid_pick_trade");
    }

    [Fact]
    public void FrontOfficeAdvice_NamesTeamsAndPlayersRatherThanIdentifiers()
    {
        // Regression: rule explanations reached the screen as "Team '01M3F…' has no backup", and a
        // full roster was reported as "carries 10 players" because the rotation was counted.
        var session = Session(Write(SamplePack()));
        var overview = session.Load().Value;

        foreach (var team in overview.Teams)
        {
            var advice = session.FrontOfficeAdvisory(team.TeamId);
            Assert.True(advice.IsSuccess, string.Join("; ", advice.Errors.Select(error => error.Message)));

            var explanations = advice.Value.Direction.Factors.Select(line => line.Explanation)
                .Concat(advice.Value.Needs.PositionalNeeds.Select(line => line.Explanation))
                .Concat(advice.Value.Needs.Notes.Select(line => line.Explanation))
                .Concat(advice.Value.TradeTargets.SelectMany(line => line.Rationale).Select(line => line.Explanation))
                .Concat(advice.Value.FreeAgentTargets.SelectMany(line => line.Rationale).Select(line => line.Explanation))
                .ToList();

            Assert.All(explanations, text => Assert.DoesNotMatch(@"\b[0-9A-HJKMNP-TV-Z]{26}\b", text));
            Assert.DoesNotContain(advice.Value.Needs.Notes, line => line.RuleCode == "ai_needs.below_roster_minimum");
        }
    }

    [Fact]
    public void PlayerProfile_ShowsAttributesContractAndPackCareer()
    {
        var pack = SamplePack();
        pack["players"]![0]!["career"] = new JsonArray(
            new JsonObject { ["season"] = "2038-39", ["team"] = "Old Club", ["gamesPlayed"] = 70, ["minutes"] = 2100, ["points"] = 1400, ["rebounds"] = 500, ["assists"] = 300 });
        var session = Session(Write(pack));
        var overview = session.Load().Value;
        var star = overview.Teams.SelectMany(team => team.Roster).Single(spot => spot.FullName == "Player T0-S0");

        var profile = session.PlayerProfile(star.PlayerId);

        Assert.True(profile.IsSuccess, string.Join("; ", profile.Errors.Select(error => error.Message)));
        Assert.Equal(new BallGM.Application.Players.PlayerAttributes(90, 70, 85, 60, 65), profile.Value.Attributes);
        Assert.Equal(74, profile.Value.Overall);
        Assert.Equal("RED Team", profile.Value.TeamName);
        Assert.Equal([10_000_000L, 10_500_000L], profile.Value.Contract.Select(line => line.Salary));
        var past = Assert.Single(profile.Value.Career);
        Assert.Equal("2038-39", past.Season);
        Assert.Empty(profile.Value.RecentGames);
    }

    [Fact]
    public void PlayerProfile_RecentFormIsTheLastGamesNewestFirstAndTheSeasonJoinsTheCareer()
    {
        var session = Session(Write(SamplePack()));
        var overview = session.Load().Value;
        var star = overview.Teams.SelectMany(team => team.Roster).Single(spot => spot.FullName == "Player T0-S0");
        Assert.True(session.StartSeason(seed: 7).IsSuccess);
        Assert.True(session.AdvanceToEndOfSeason().IsSuccess);

        var profile = session.PlayerProfile(star.PlayerId).Value;

        Assert.Equal(LeagueSession.RecentGameCount, profile.RecentGames.Count);
        Assert.True(profile.RecentGames.Zip(profile.RecentGames.Skip(1)).All(pair => pair.First.Day >= pair.Second.Day));
        Assert.NotNull(profile.CurrentSeason);
        Assert.True(profile.Career[^1].IsCurrent);

        Assert.True(session.ConcludeSeason().IsSuccess);
        var afterConclusion = session.PlayerProfile(star.PlayerId).Value;
        var concluded = Assert.Single(afterConclusion.Career);
        Assert.False(concluded.IsCurrent);
        Assert.Equal(profile.CurrentSeason!.GamesPlayed, concluded.GamesPlayed);
        Assert.Empty(afterConclusion.RecentGames);
    }

    [Fact]
    public void PlayerProfile_ContractDetailReadsTheLeaguesScales()
    {
        var session = Session(Write(SamplePack()));
        var overview = session.Load().Value;
        var star = overview.Teams.SelectMany(team => team.Roster).Single(spot => spot.FullName == "Player T0-S0");

        var detail = session.PlayerProfile(star.PlayerId).Value.ContractDetail!;

        // Default ruleset: soft cap 141M, 25% ceiling for 5 seasons of service, 2.1M floor from 3 seasons.
        Assert.Equal(141_000_000, detail.SoftCap);
        Assert.Equal(35_250_000, detail.MaximumSalary);
        Assert.Equal(25, detail.MaximumPercentOfCap);
        Assert.Equal(2_100_000, detail.MinimumSalary);
        Assert.Equal(20_500_000, detail.TotalValue);
        Assert.Equal(10_250_000, detail.AverageAnnualValue);
        Assert.Equal(2042, detail.FreeAgentYear);
        Assert.Equal(10_000_000 / 141_000_000.0, detail.Years[0].ShareOfCap!.Value, 10);
    }

    [Fact]
    public void PlayerProfile_SeasonDetailCarriesTeamContextAndSurvivesTheSeason()
    {
        var session = Session(Write(SamplePack()));
        var overview = session.Load().Value;
        var star = overview.Teams.SelectMany(team => team.Roster).Single(spot => spot.FullName == "Player T0-S0");
        Assert.True(session.StartSeason(seed: 5).IsSuccess);
        Assert.True(session.AdvanceToEndOfSeason().IsSuccess);

        var detail = session.PlayerProfile(star.PlayerId).Value.SeasonDetail!;

        Assert.True(detail.Games > 0);
        Assert.Equal(detail.Games, detail.Starts);
        // At least five players × 48 minutes a game; overtime adds to it.
        Assert.True(detail.TeamMinutes >= 240 * detail.Games);
        Assert.True(detail.FieldGoalsAttempted >= detail.FieldGoalsMade);
        Assert.Equal(detail.Points, (2 * (detail.FieldGoalsMade - detail.ThreesMade)) + (3 * detail.ThreesMade) + detail.FreeThrowsMade);

        Assert.True(session.ConcludeSeason().IsSuccess);
        var concluded = Assert.Single(session.PlayerProfile(star.PlayerId).Value.Career);
        Assert.Equal(detail.FieldGoalsAttempted, concluded.Metrics!["FGA"]);
        Assert.True(concluded.Metrics.ContainsKey("TS_PCT"));
    }

    [Fact]
    public void Pack_WithCapMechanics_BillsARepeaterAndListsItsRestrictions()
    {
        var ruleset = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "rulesets", "default-league.json")))!.AsObject();
        ruleset["capMechanics"] = System.Text.Json.Nodes.JsonNode.Parse("""
            { "taxBracketSize": 5000000, "taxRatesPercent": [150, 175], "repeaterTaxRatesPercent": [250, 275], "taxRateIncrementPercent": 50,
              "aboveFirstApronMatchPercent": 100, "secondApronBlocksAggregation": true }
            """);
        File.WriteAllText(Path.Combine(_directory, "ruleset.json"), ruleset.ToJsonString());
        var pack = SamplePack();
        pack["rulesetFile"] = "ruleset.json";
        pack["teams"]![0]!["taxRepeater"] = true;
        foreach (var player in pack["players"]!.AsArray().Where(player => player!["team"]?.GetValue<string>() == "RED"))
        {
            player!["contract"] = new JsonObject { ["salaries"] = new JsonArray(16_000_000L) };
        }

        var session = Session(Write(pack));
        var overview = session.Load();
        Assert.True(overview.IsSuccess, string.Join("; ", overview.Errors.Select(error => error.Message)));

        var red = overview.Value.Teams.Single(team => team.TeamName == "RED Team").CapSheet;
        Assert.Equal(192_000_000, red.TotalPayroll);
        Assert.True(red.TaxBill!.IsRepeater);
        Assert.Equal(20_000_000, red.TaxBill.AmountOverTaxLine);
        Assert.Contains(red.Restrictions!, line => line.RuleCode == "cap_status.no_salary_aggregation");
        Assert.Contains(red.Restrictions!, line => line.RuleCode == "cap_status.apron_salary_matching");
        var blue = overview.Value.Teams.Single(team => team.TeamName == "BLU Team").CapSheet;
        Assert.Equal(0, blue.TaxBill!.TaxOwed);
    }

    [Fact]
    public void CapOutlook_ShowsCommittedMoneyAndRoomSeasonBySeason()
    {
        var session = Session(Write(SamplePack()));
        var overview = session.Load().Value;
        var red = overview.Teams.Single(team => team.TeamName == "RED Team");

        var outlook = session.CapOutlook(red.TeamId, capGrowthPercent: 10).Value;

        Assert.Equal(["2040-41", "2041-42", "2042-43", "2043-44", "2044-45"], outlook.Seasons);
        Assert.Equal(12, outlook.Totals[0].PlayersUnderContract);
        Assert.Equal(120_000_000, outlook.Totals[0].Guaranteed);
        Assert.Equal(126_000_000, outlook.Totals[1].Guaranteed);
        Assert.Equal(0, outlook.Totals[2].PlayersUnderContract);
        Assert.Equal(141_000_000, outlook.Totals[0].SoftCap);
        Assert.Equal(155_100_000, outlook.Totals[1].SoftCap);           // cap grown 10%
        Assert.Equal(outlook.Totals[0].SoftCap - outlook.Totals[0].TotalPayroll, outlook.Totals[0].CapRoom);
        Assert.Equal(12, outlook.Players.Count);
        Assert.All(outlook.Players, row => Assert.Null(row.Cells[2].Salary));
    }

    [Fact]
    public void Extension_RefusedBelowTheAskIsAnAlertAndAcceptedMovesThePlayerOutOfHisClass()
    {
        var pack = SamplePack();
        pack["players"]![0]!["contract"] = new JsonObject { ["salaries"] = new JsonArray(10_000_000L) };   // expires this season
        var session = Session(Write(pack));
        var overview = session.Load().Value;
        var star = overview.Teams.SelectMany(team => team.Roster).Single(spot => spot.FullName == "Player T0-S0");

        var classes = session.UpcomingFreeAgents();
        var thisSummer = classes.Single(freeAgentClass => freeAgentClass.Year == 2041);
        var line = Assert.Single(thisSummer.Players);
        Assert.Equal(star.PlayerId, line.PlayerId);
        Assert.True(line.ExtensionEligible);
        Assert.Equal(47, classes.Single(freeAgentClass => freeAgentClass.Year == 2042).Players.Count);

        var terms = session.ExtensionTerms(star.PlayerId).Value;
        Assert.True(terms.Eligible);
        Assert.Equal(2041, terms.StartSeason);
        var ask = terms.AskingPrice!.Value;

        var refused = session.OfferExtension(star.PlayerId, 3, ask * 70 / 100, 5).Value;
        Assert.True(refused.IsLegal);
        Assert.False(refused.Accepted);
        var alert = Assert.Single(session.ExtensionAlerts());
        Assert.Equal(star.PlayerId, alert.PlayerId);
        Assert.True(session.UpcomingFreeAgents().Single(c => c.Year == 2041).Players.Single().RefusedExtension);

        var accepted = session.OfferExtension(star.PlayerId, 3, ask, 5).Value;
        Assert.True(accepted.Accepted, accepted.Explanation);
        Assert.Empty(session.ExtensionAlerts());
        Assert.DoesNotContain(session.UpcomingFreeAgents(), c => c.Year == 2041);
        Assert.Contains(session.UpcomingFreeAgents().Single(c => c.Year == 2044).Players, p => p.PlayerId == star.PlayerId);
        Assert.Equal([2040, 2041, 2042, 2043], session.PlayerProfile(star.PlayerId).Value.Contract.Select(season => season.Season));

        // Season end: the extended player is not released when his old contract expires.
        Assert.True(session.StartSeason(seed: 3).IsSuccess);
        Assert.True(session.AdvanceToEndOfSeason().IsSuccess);
        Assert.True(session.ConcludeSeason().IsSuccess);
        Assert.Equal("RED Team", session.PlayerProfile(star.PlayerId).Value.TeamName);
    }

    [Fact]
    public void Extension_IsIllegalBeforeTheFinalSeasonOfTheContract()
    {
        var session = Session(Write(SamplePack()));
        var overview = session.Load().Value;
        var star = overview.Teams.SelectMany(team => team.Roster).Single(spot => spot.FullName == "Player T0-S0");

        var terms = session.ExtensionTerms(star.PlayerId).Value;
        var outcome = session.OfferExtension(star.PlayerId, 2, 30_000_000, 0).Value;

        Assert.False(terms.Eligible);
        Assert.False(outcome.IsLegal);
        Assert.Contains(outcome.Violations, violation => violation.Contains("final season"));
        Assert.Empty(session.ExtensionAlerts());
    }

    [Fact]
    public void Pack_RefusesACareerSeasonWithNegativeTotals()
    {
        var pack = SamplePack();
        pack["players"]![0]!["career"] = new JsonArray(new JsonObject { ["season"] = "2038-39", ["gamesPlayed"] = -1, ["minutes"] = 0, ["points"] = 0, ["rebounds"] = 0, ["assists"] = 0 });

        AssertFails(Load(pack), "league_pack.invalid_field");
    }

    [Fact]
    public void PlayerProfile_ForAnUnknownPlayerIsAStructuredFailure()
    {
        var session = Session(Write(SamplePack()));
        session.Load();

        var profile = session.PlayerProfile("NOBODY");

        Assert.True(profile.IsFailure);
        Assert.Equal("player_profile.unknown_player", profile.Errors[0].Code);
    }

    private static LeagueSession Session(string packPath) => new(
        new LeaguePackDataSource(packPath),
        new RulesCapLedger(),
        new RulesDraftAssetLedger(),
        new RulesTradeEngine(),
        new RulesSigningEngine(),
        new RulesFreeAgencyMarket(),
        new RulesSeasonEngine(),
        new SaveGameSerializer(),
        new RulesFrontOfficeAdvisor(new RulesCapLedger()));

    [Fact]
    public void MissingPackFile_IsAStructuredFailure()
    {
        AssertFails(new LeaguePackDataSource(Path.Combine(_directory, "nope.json")).Load(), "league_pack.file_missing");
    }

    private BallGM.Domain.Common.DomainOperationResult<LeagueSnapshot> Load(JsonObject pack) =>
        new LeaguePackDataSource(Write(pack)).Load();

    private string Write(JsonObject pack)
    {
        var path = Path.Combine(_directory, "pack.json");
        File.WriteAllText(path, pack.ToJsonString());
        return path;
    }

    private static void AssertFails(BallGM.Domain.Common.DomainOperationResult<LeagueSnapshot> result, string code)
    {
        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == code);
    }

    private static string Describe(BallGM.Domain.Common.DomainOperationResult<LeagueSnapshot> result) =>
        string.Join("; ", result.Errors.Select(error => $"{error.Code}: {error.Message}"));

    private static readonly string[] TeamKeys = ["RED", "BLU", "GRN", "GLD"];
    private static readonly string[] Positions = ["PointGuard", "ShootingGuard", "SmallForward", "PowerForward", "Center"];

    /// <summary>Four fictional teams of twelve in two one-division conferences, plus one free agent.</summary>
    private static JsonObject SamplePack()
    {
        var players = new JsonArray();
        for (var team = 0; team < TeamKeys.Length; team++)
        {
            for (var slot = 0; slot < 12; slot++)
            {
                var overall = 80 - (slot * 2) - (team * 3);
                var node = PlayerNode($"Player T{team}-S{slot}", TeamKeys[team], overall, withContract: true);
                node["position"] = Positions[slot % Positions.Length];
                players.Add(node);
            }
        }

        players[0]!["ratings"] = new JsonObject
        {
            ["height"] = 90,
            ["speed"] = 70,
            ["strength"] = 85,
            ["passing"] = 60,
            ["lateralQuickness"] = 65,
        };

        players.Add(PlayerNode("Unsigned Veteran", null, 70, withContract: false));

        return new JsonObject
        {
            ["schemaVersion"] = 1,
            ["name"] = "Sample Pack League",
            ["season"] = 2040,
            ["conferences"] = new JsonArray(
                new JsonObject { ["name"] = "North", ["divisions"] = new JsonArray(new JsonObject { ["name"] = "North One", ["teams"] = new JsonArray("RED", "BLU") }) },
                new JsonObject { ["name"] = "South", ["divisions"] = new JsonArray(new JsonObject { ["name"] = "South One", ["teams"] = new JsonArray("GRN", "GLD") }) }),
            ["teams"] = new JsonArray(TeamKeys
                .Select(key => (JsonNode)new JsonObject { ["key"] = key, ["name"] = $"{key} Team", ["franchiseName"] = $"{key} Club" })
                .ToArray()),
            ["players"] = players,
        };
    }

    private static JsonObject PlayerNode(string name, string? team, int overall, bool withContract)
    {
        var node = new JsonObject
        {
            ["name"] = name,
            ["position"] = "SmallForward",
            ["birthDate"] = "2014-01-01",
            ["seasonsOfService"] = 5,
            ["ratings"] = new JsonObject
            {
                ["height"] = overall,
                ["speed"] = overall,
                ["strength"] = overall,
                ["passing"] = overall,
                ["lateralQuickness"] = overall,
            },
        };

        if (team is not null)
        {
            node["team"] = team;
        }

        if (withContract)
        {
            node["contract"] = new JsonObject { ["salaries"] = new JsonArray(10_000_000L, 10_500_000L) };
        }

        return node;
    }
}
