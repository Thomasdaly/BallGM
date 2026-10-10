using System.Globalization;
using BallGM.Application.Players;
using BallGM.Client.Avalonia.ViewModels;
using BallGM.Domain.Seasons;

namespace BallGM.Client.Avalonia.Tests;

public sealed class RatingViewTests
{
    private static CountingStats Game(int minutes, int points, int fgm, int fga, int plusMinus = 0) =>
        CountingStats.None with { Games = 1, Minutes = minutes, Points = points, FieldGoalsMade = fgm, FieldGoalsAttempted = fga, PlusMinus = plusMinus };

    [Fact]
    public void TheBreakdownAtOneDecimalAddsUpToTheFigureShown()
    {
        foreach (var game in new[] { Game(34, 24, 9, 17, 8), Game(12, 2, 1, 5, -9), Game(40, 41, 15, 24, 15), Game(22, 9, 4, 8, 0) })
        {
            var view = RatingView.ForGame(game)!;
            var shown = double.Parse(view.Text, CultureInfo.InvariantCulture);
            var sum = view.Breakdown.Sum(row => double.Parse(row.Value, CultureInfo.InvariantCulture));

            Assert.Equal(shown - (StatFormulas.RatingAnchor / 100.0), sum, 6);
        }
    }

    [Fact]
    public void TheBandFollowsTheFigureAndAPlayerWhoDidNotPlayHasNoPill()
    {
        Assert.Null(RatingView.ForGame(CountingStats.None));
        Assert.Null(RatingView.ForAverage(null));

        Assert.True(RatingView.ForAverage(new RatingAverage(812, 10))!.IsOutstanding);
        Assert.True(RatingView.ForAverage(new RatingAverage(705, 10))!.IsGood);
        Assert.Equal(RatingBand.Average, RatingView.ForAverage(new RatingAverage(650, 10))!.Band);
        Assert.True(RatingView.ForAverage(new RatingAverage(590, 10))!.IsPoor);
        Assert.Equal("8.1", RatingView.ForAverage(new RatingAverage(812, 10))!.Text);
        Assert.Empty(RatingView.ForAverage(new RatingAverage(812, 10))!.Breakdown);
    }
}
