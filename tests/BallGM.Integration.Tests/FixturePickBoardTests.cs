using BallGM.Application.Leagues;
using BallGM.Infrastructure.Cap;
using BallGM.Infrastructure.DraftAssets;
using BallGM.Infrastructure.Fixtures;

namespace BallGM.Integration.Tests;

/// <summary>
/// The shipped fixture, loaded end to end through the real query, the real rules, and the real
/// ruleset file. These tests are what stop the pick board being demonstrated on a league where
/// nothing interesting has happened.
/// </summary>
public sealed class FixturePickBoardTests
{
    private const string Hoarder = "Harbourline Sporting Club";
    private const string Mortgaged = "Saltpan City Sporting Trust";
    private const string SwapTarget = "Verdanmoor Basketball Club";

    [Fact]
    public void Board_CoversEveryFranchiseAcrossTheConfiguredTradableHorizon()
    {
        var board = LoadBoard();

        Assert.Equal(6, board.Franchises.Count);
        Assert.Equal(2032, board.FirstDraftSeason);
        Assert.Equal(5, board.DraftCount);
        Assert.Equal(2, board.RoundCount);
        Assert.All(board.Franchises, row => Assert.Equal(5, row.Drafts.Count));
    }

    [Fact]
    public void Fixture_SettlesTheCurrentDraftSoAProtectedPickConveysAndAnotherRollsOver()
    {
        var overview = LoadLeague();
        var board = overview.PickBoard;

        // The pick that did not convey: its obligation rolled onto the following draft, one step
        // further along its protection schedule.
        var rolled = Assets(board, Mortgaged)
            .Single(asset => asset.ProtectionSummary?.Contains("already rolled over 1 draft", StringComparison.Ordinal) == true);

        Assert.Equal("Owed", rolled.State);
        Assert.Equal(2032, DraftSeasonOf(board, Mortgaged, rolled));
        Assert.Contains("protected through selection 3", rolled.ProtectionSummary);
        Assert.Contains("rolls to the 2033 draft unprotected", rolled.OutcomeIfProtectionHolds);

        // The rollover left a line on the asset it landed on, so the drill-down explains why a
        // future pick is suddenly encumbered.
        Assert.Contains(rolled.History, line => line.Kind == "Protection held");
    }

    [Fact]
    public void Fixture_ShowsAFranchiseThatHasSpentTwoOfItsNextThreeFirstRoundPicks()
    {
        var board = LoadBoard();
        var firsts = Assets(board, Mortgaged)
            .Where(asset => asset.Round == 1 && asset.OriginalFranchiseName == Mortgaged)
            .ToList();

        Assert.Equal("Owed", firsts[0].State);
        Assert.Equal("Own", firsts[1].State);
        Assert.Equal("Gone", firsts[2].State);
    }

    [Fact]
    public void Fixture_ShowsAFranchiseHoardingOtherFranchisesFirstRoundPicks()
    {
        var board = LoadBoard();
        var acquired = Assets(board, Hoarder)
            .Where(asset => asset.OriginalFranchiseName != Hoarder)
            .ToList();

        Assert.Contains(acquired, asset => asset.State == "Acquired" && asset.Round == 1);
        Assert.Contains(acquired, asset => asset.State == "Owed to you");
        Assert.Contains(acquired, asset => asset.State == "Swap right");

        // Every acquired asset carries the trail that explains how it arrived.
        Assert.All(acquired, asset => Assert.NotEmpty(asset.History));
    }

    [Fact]
    public void Fixture_LeavesOneSwapRightLiveOnTheBoardWithBothSidesVisible()
    {
        var board = LoadBoard();

        var held = Assets(board, Hoarder).Single(asset => asset.State == "Swap right");
        var encumbered = Assets(board, SwapTarget).Single(asset => asset.State == "Swappable");

        Assert.Equal(held.PickId, encumbered.PickId);
        Assert.Contains("may swap this selection", encumbered.ProtectionSummary);
        Assert.NotNull(held.OutcomeIfProtectionHolds);
    }

    [Fact]
    public void Fixture_RecordsEveryDraftAssetEventInTheSameLedgerAsTheCapEvents()
    {
        var board = LoadBoard();

        var kinds = board.Franchises
            .SelectMany(row => row.Drafts)
            .SelectMany(cell => cell.Assets)
            .SelectMany(asset => asset.History)
            .Select(line => line.Kind)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.Contains("Pick traded", kinds);
        Assert.Contains("Pick encumbered", kinds);
        Assert.Contains("Protection held", kinds);
    }

    [Fact]
    public void Fixture_ProducesTheSameBoardOnEveryLoad()
    {
        static IEnumerable<string> Describe(PickBoardSummary board) =>
            board.Franchises.SelectMany(row => row.Drafts.SelectMany(cell =>
                cell.Assets.Select(asset =>
                    $"{row.FranchiseName}|{cell.DraftSeason}|{asset.Label}|{asset.State}|{asset.ProtectionSummary}")));

        // Identifiers are minted per load, so the board is compared on what it says rather than on
        // the identifiers behind it.
        Assert.Equal(Describe(LoadBoard()), Describe(LoadBoard()));
    }

    private static IReadOnlyList<PickAssetSummary> Assets(PickBoardSummary board, string franchiseName) =>
        board.Franchises
            .Single(row => row.FranchiseName == franchiseName)
            .Drafts
            .SelectMany(cell => cell.Assets)
            .ToList();

    private static int DraftSeasonOf(PickBoardSummary board, string franchiseName, PickAssetSummary asset) =>
        board.Franchises
            .Single(row => row.FranchiseName == franchiseName)
            .Drafts
            .Single(cell => cell.Assets.Any(candidate => candidate.PickId == asset.PickId))
            .DraftSeason;

    private static PickBoardSummary LoadBoard() => LoadLeague().PickBoard;

    private static LeagueOverview LoadLeague()
    {
        var result = new GetLeagueOverviewQuery(
            new FixtureLeagueDataSource(),
            new RulesCapLedger(),
            new RulesDraftAssetLedger()).Execute();

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));
        return result.Value;
    }
}
