using BallGM.Simulation.Seasons;

namespace BallGM.Simulation.Tests;

/// <summary>
/// What the model produces <em>on average</em>, asserted against the sport it is meant to resemble.
/// <para>
/// These are the tests that would catch a well-meaning change to a bound quietly turning the league
/// into a different game — a scoring rate that drifts, a home advantage that stops mattering, a
/// mismatch that starts producing scorelines nobody has ever seen. Every run is seeded, so none of
/// them is flaky: the same games are played every time, and a failure means the model moved, never
/// that the dice did.
/// </para>
/// <para>
/// The bands are deliberately wide. This is a fictional league and the point is not to reproduce any
/// real one's box score to the decimal, it is to stay inside the range a reader of the sport would
/// recognise. A tight band here would fail on every legitimate tuning pass and teach whoever hit it
/// to widen the band rather than to think.
/// </para>
/// </summary>
public sealed class MatchModelCalibrationTests
{
    [Fact]
    public void TeamsScoreLikeABasketballTeam()
    {
        var scores = MatchTestFixtures.PlayMany(1_200)
            .SelectMany(outcome => new[] { outcome.Result.HomePoints, outcome.Result.AwayPoints })
            .OrderBy(points => points)
            .ToArray();

        Assert.InRange(scores.Average(), 95, 120);
        Assert.InRange(scores[scores.Length / 2], 95, 120);

        // The tails matter as much as the middle: a model with a realistic mean and no shape puts
        // 40-point and 190-point games on the schedule.
        Assert.InRange(scores[scores.Length / 20], 78, 100);
        Assert.InRange(scores[scores.Length * 19 / 20], 118, 142);
    }

    [Fact]
    public void GamesAreDecidedByAMarginTheSportWouldRecognise()
    {
        var margins = MatchTestFixtures.PlayMany(1_200)
            .Select(outcome => Math.Abs(outcome.Result.HomePoints - outcome.Result.AwayPoints))
            .OrderBy(margin => margin)
            .ToArray();

        Assert.InRange(margins.Average(), 9, 17);

        // Close games have to be common enough that a season has drama in it.
        var withinFive = margins.Count(margin => margin <= 5) * 100.0 / margins.Length;
        Assert.InRange(withinFive, 15, 32);
    }

    [Fact]
    public void PlayingAtHomeIsWorthSomethingButNotTheGame()
    {
        var homeWins = MatchTestFixtures.PlayMany(1_500).Count(outcome => outcome.Result.HomeWon);
        var share = homeWins * 100.0 / 1_500;

        Assert.InRange(share, 51, 61);
    }

    [Fact]
    public void TheBetterTeamUsuallyWinsAndTheWorseTeamSometimesDoes()
    {
        var favoured = MatchTestFixtures.PlayMany(1_000, homeRating: 85, awayRating: 55, seedOffset: 90_000)
            .Count(outcome => outcome.Result.HomeWon) * 100.0 / 1_000;

        // The strength swing is capped for exactly this reason: an uncapped term turns the best team
        // in the league into one that cannot lose, and a season nobody can be upset in is a season
        // with no story in it.
        Assert.InRange(favoured, 78, 95);
    }

    [Fact]
    public void AnEvenlyMatchedGameIsNearlyACoinToss()
    {
        var homeWins = MatchTestFixtures.PlayMany(1_000, homeRating: 70, awayRating: 70, seedOffset: 31_000)
            .Count(outcome => outcome.Result.HomeWon) * 100.0 / 1_000;

        Assert.InRange(homeWins, 50, 62);
    }

    [Fact]
    public void TiredLegsCostGamesWithoutDecidingThem()
    {
        var rested = MatchTestFixtures.PlayMany(1_000, seedOffset: 12_000)
            .Count(outcome => outcome.Result.HomeWon) * 100.0 / 1_000;

        var opponentOnABackToBack = MatchTestFixtures.PlayMany(1_000, awayRestDays: 1, seedOffset: 12_000)
            .Count(outcome => outcome.Result.HomeWon) * 100.0 / 1_000;

        // Worth something, and bounded: a schedule that decided games outright would make the
        // fixture list the opponent rather than the other team.
        Assert.True(
            opponentOnABackToBack > rested,
            $"A rested side won {rested:F1}% against a tired one and {opponentOnABackToBack:F1}% against a rested one.");

        Assert.InRange(opponentOnABackToBack - rested, 1, 15);
    }

    [Fact]
    public void ALeaguesBestPlayersScoreLikeBestPlayers()
    {
        var leaders = MatchTestFixtures.PlayMany(800)
            .Select(outcome => outcome.Result.BoxScore!
                .LinesFor(outcome.Result.HomeTeamId)
                .Max(line => line.Points))
            .OrderBy(points => points)
            .ToArray();

        // A team's leading scorer. Lower than a real league's because a player here carries one
        // overall rating and nothing that says how much of the offence runs through them — usage is
        // inferred from rating and minutes, so it concentrates less than a real first option does.
        Assert.InRange(leaders.Average(), 17, 30);
        Assert.True(leaders[^1] >= 30, $"Nobody scored 30 in 800 games; the best was {leaders[^1]}.");
    }

    [Fact]
    public void RunningTheSameRatingsAtADifferentScaleDoesNotChangeTheSport()
    {
        // Strength enters the model only as a difference, so a league rated 40/40 and a league rated
        // 90/90 are the same contest. That is what lets a data pack ship its own rating scale
        // without discovering the sport had stopped working.
        var low = MatchTestFixtures.PlayMany(600, homeRating: 45, awayRating: 45, seedOffset: 7_000)
            .SelectMany(outcome => new[] { outcome.Result.HomePoints, outcome.Result.AwayPoints })
            .Average();

        var high = MatchTestFixtures.PlayMany(600, homeRating: 90, awayRating: 90, seedOffset: 7_000)
            .SelectMany(outcome => new[] { outcome.Result.HomePoints, outcome.Result.AwayPoints })
            .Average();

        Assert.InRange(Math.Abs(low - high), 0, 2);
    }

    [Fact]
    public void InjuriesHappenOftenEnoughToMatterAndRarelyEnoughToPlayOn()
    {
        var outcomes = MatchTestFixtures.PlayMany(1_000);
        var perGame = outcomes.Sum(outcome => outcome.Injuries.Count) / 1_000.0;

        Assert.InRange(perGame, 0.02, 0.5);

        // Skewed short: most knocks cost a few days, and the long ones are the exception.
        var spells = outcomes.SelectMany(outcome => outcome.Injuries).Select(injury => injury.DaysOut).ToArray();

        Assert.NotEmpty(spells);
        Assert.True(
            spells.Count(days => days <= 10) > spells.Length / 2,
            "Most injuries should be short ones.");
    }
}
