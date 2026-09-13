using BallGM.Domain.Common;
using BallGM.Domain.Draft;
using BallGM.Domain.DraftAssets;
using BallGM.Domain.Franchises;
using BallGM.Domain.Leagues;
using BallGM.Domain.Randomness;
using BallGM.Domain.Seasons;
using BallGM.Domain.Teams;
using BallGM.Rules.Configuration;
using BallGM.Rules.Draft;

namespace BallGM.Rules.Tests.Draft;

/// <summary>
/// The draft-day flow that turns a generated class into actual rosters: best prospect left on the
/// board to the team on the clock, in the order the lottery drew, respecting a pick that has already
/// changed hands.
/// </summary>
public sealed class DraftDayTests
{
    private static readonly Season DraftSeason = new(2031);

    private static readonly DraftRules TwoRoundNoLottery = DraftRules.Create(
        roundCount: 2, lotteryEnabled: false, tradableFutureDraftHorizon: 3, retainedRoundNumber: 1, retainedRoundInterval: 2).Value;

    private static readonly DraftClassRules SmallClass =
        DraftClassRules.Create(classSize: 3, minimumRating: 40, maximumRating: 90, prospectAgeYears: 19).Value;

    private readonly DraftDay _day = new();

    [Fact]
    public void RunAssignsTheBestProspectLeftToEachSelectionInOrder()
    {
        var (worst, second, best) = ThreeFranchises();
        var teams = new[] { TeamFor(worst, "Worst"), TeamFor(second, "Second"), TeamFor(best, "Best") };
        var standings = FinalStandings();

        var oneRound = DraftRules.Create(
            roundCount: 1, lotteryEnabled: false, tradableFutureDraftHorizon: 3, retainedRoundNumber: 1, retainedRoundInterval: 2).Value;

        var result = _day.Run(
            DraftSeason, standings, teams, NewBook(), oneRound, SmallClass, DraftLotteryRules.None, new SeededRandomSource(20260912));

        Assert.True(result.IsSuccess);
        var selections = result.Value.Selections;

        Assert.Equal(3, selections.Count);
        Assert.Equal("Worst", TeamNameOf(teams, selections[0].TeamId));
        Assert.Equal("Second", TeamNameOf(teams, selections[1].TeamId));
        Assert.Equal("Best", TeamNameOf(teams, selections[2].TeamId));

        // Best-available: each selection's true rating is no better than the one picked before it.
        Assert.True(selections[0].Prospect.TrueRating.Overall >= selections[1].Prospect.TrueRating.Overall);
        Assert.True(selections[1].Prospect.TrueRating.Overall >= selections[2].Prospect.TrueRating.Overall);
    }

    [Fact]
    public void RunProducesNoSelectionsAndANoteWhenTheLeagueHoldsNoDraft()
    {
        var (worst, second, best) = ThreeFranchises();
        var teams = new[] { TeamFor(worst, "Worst"), TeamFor(second, "Second"), TeamFor(best, "Best") };
        var standings = FinalStandings();

        var result = _day.Run(
            DraftSeason, standings, teams, NewBook(), DraftRules.NoDraft, SmallClass, DraftLotteryRules.None, new ThrowingRandomSource());

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Selections);
        Assert.Contains(result.Value.Notes, note => note.RuleCode == "draft_day.no_draft");
    }

    [Fact]
    public void RunProducesNoSelectionsAndANoteWhenThisLeagueGeneratesNoClassesOfItsOwn()
    {
        var (worst, second, best) = ThreeFranchises();
        var teams = new[] { TeamFor(worst, "Worst"), TeamFor(second, "Second"), TeamFor(best, "Best") };
        var standings = FinalStandings();

        var result = _day.Run(
            DraftSeason, standings, teams, NewBook(), TwoRoundNoLottery, DraftClassRules.None, DraftLotteryRules.None, new ThrowingRandomSource());

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Selections);
        Assert.Contains(result.Value.Notes, note => note.RuleCode == "draft_day.no_generator");
    }

    [Fact]
    public void RunNotesAndStopsWhenTheClassRunsOutBeforeEverySlotIsFilled()
    {
        var (worst, second, best) = ThreeFranchises();
        var teams = new[] { TeamFor(worst, "Worst"), TeamFor(second, "Second"), TeamFor(best, "Best") };
        var standings = FinalStandings();

        // Two rounds over three teams is six slots; the class states only three prospects.
        var result = _day.Run(
            DraftSeason, standings, teams, NewBook(), TwoRoundNoLottery, SmallClass, DraftLotteryRules.None, new SeededRandomSource(7));

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Selections.Count);
        Assert.Contains(result.Value.Notes, note => note.RuleCode == "draft_day.class_exhausted");
    }

    [Fact]
    public void RunSendsASelectionToWhicheverFranchiseCurrentlyOwnsThePick()
    {
        var (worst, second, best) = ThreeFranchises();
        var teams = new[] { TeamFor(worst, "Worst"), TeamFor(second, "Second"), TeamFor(best, "Best") };
        var standings = FinalStandings();

        var oneRound = DraftRules.Create(
            roundCount: 1, lotteryEnabled: false, tradableFutureDraftHorizon: 3, retainedRoundNumber: 1, retainedRoundInterval: 2).Value;

        // Worst's own first-rounder (originally selection #1) has been traded away to Best.
        var book = NewBook();
        var pickResult = DraftPick.Create(new DraftPickId(SortableId.NewId()), book.LeagueId, DraftSeason, round: 1, originalFranchiseId: worst);
        Assert.True(pickResult.IsSuccess);
        Assert.True(book.Register(pickResult.Value).IsSuccess);
        Assert.True(book.Transfer(pickResult.Value.Id, best).IsSuccess);

        var result = _day.Run(
            DraftSeason, standings, teams, book, oneRound, SmallClass, DraftLotteryRules.None, new SeededRandomSource(20260912));

        Assert.True(result.IsSuccess);
        Assert.Equal("Best", TeamNameOf(teams, result.Value.Selections[0].TeamId));
    }

    private static (FranchiseId Worst, FranchiseId Second, FranchiseId Best) ThreeFranchises() =>
        (new FranchiseId("franchise-worst"), new FranchiseId("franchise-second"), new FranchiseId("franchise-best"));

    private static Team TeamFor(FranchiseId franchiseId, string name) =>
        Team.Create(new TeamId(name), franchiseId, name, new RosterSizeLimits(0, 15)).Value;

    private static IReadOnlyList<SeasonHistoryTeamRecord> FinalStandings() =>
    [
        new(new TeamId("Best"), 1, new TeamRecord(50, 10), 0, 0),
        new(new TeamId("Second"), 2, new TeamRecord(30, 30), 0, 0),
        new(new TeamId("Worst"), 3, new TeamRecord(10, 50), 0, 0),
    ];

    private static string TeamNameOf(IReadOnlyCollection<Team> teams, TeamId teamId) =>
        teams.First(team => team.Id == teamId).Name;

    private static DraftAssetBook NewBook() => new(new LeagueId(SortableId.NewId()));

    private sealed class ThrowingRandomSource : IRandomSource
    {
        public int NextInt32(int minInclusive, int maxExclusive) =>
            throw new InvalidOperationException("No randomness should have been drawn.");
    }
}
