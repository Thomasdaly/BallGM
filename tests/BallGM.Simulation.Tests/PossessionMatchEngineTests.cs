using BallGM.Domain.Seasons;
using BallGM.Domain.Teams;
using BallGM.Simulation.Seasons;

namespace BallGM.Simulation.Tests;

/// <summary>
/// What the match engine guarantees about one game, as opposed to what it produces on average.
/// Every assertion here holds for every game rather than over a distribution — the distribution is
/// <see cref="MatchModelCalibrationTests"/>.
/// </summary>
public sealed class PossessionMatchEngineTests
{
    private static readonly PossessionMatchEngine Engine = new();

    [Fact]
    public void TheSameSeedPlaysTheSameGameTwice()
    {
        var first = Engine.Play(MatchTestFixtures.Setup(seed: 4242));
        var second = Engine.Play(MatchTestFixtures.Setup(seed: 4242));

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);

        Assert.Equal(first.Value.Result.HomePoints, second.Value.Result.HomePoints);
        Assert.Equal(first.Value.Result.AwayPoints, second.Value.Result.AwayPoints);

        // Not just the score: the box score behind it, line for line, and everybody hurt in it.
        Assert.Equal(
            Describe(first.Value.Result.BoxScore!),
            Describe(second.Value.Result.BoxScore!));

        Assert.Equal(
            first.Value.Injuries.Select(injury => $"{injury.PlayerId.Value}:{injury.DaysOut}"),
            second.Value.Injuries.Select(injury => $"{injury.PlayerId.Value}:{injury.DaysOut}"));
    }

    [Fact]
    public void DifferentSeedsPlayDifferentGames()
    {
        var scores = Enumerable.Range(0, 40)
            .Select(seed => Engine.Play(MatchTestFixtures.Setup(seed)).Value.Result)
            .Select(result => $"{result.HomePoints}-{result.AwayPoints}")
            .Distinct()
            .Count();

        // A model whose games barely varied would pass every other test here while producing a
        // league in which every night looked the same.
        Assert.True(scores > 25, $"40 seeds produced only {scores} distinct scorelines.");
    }

    [Fact]
    public void NoGameEverFinishesLevel()
    {
        // A draw is refused by GameResult, every standings tie-break assumes a winner, and a series
        // that could not be won would never end. Overtime has to be exhaustive, not best-effort.
        Assert.All(
            MatchTestFixtures.PlayMany(600),
            outcome => Assert.NotEqual(outcome.Result.HomePoints, outcome.Result.AwayPoints));
    }

    [Fact]
    public void EveryBoxScoreAddsUpToTheResultItBelongsTo()
    {
        foreach (var outcome in MatchTestFixtures.PlayMany(400))
        {
            var result = outcome.Result;
            var boxScore = Assert.IsType<BoxScore>(result.BoxScore);

            Assert.Equal(result.HomePoints, boxScore.PointsFor(result.HomeTeamId));
            Assert.Equal(result.AwayPoints, boxScore.PointsFor(result.AwayTeamId));
        }
    }

    [Fact]
    public void EveryPlayerInTheRotationGetsALineAndNobodyElseDoes()
    {
        var setup = MatchTestFixtures.Setup(seed: 91);
        var outcome = Engine.Play(setup);

        Assert.True(outcome.IsSuccess);

        var lines = outcome.Value.Result.BoxScore!.LinesFor(setup.Home.TeamId);

        Assert.Equal(
            setup.Home.Rotation.Slots.Select(slot => slot.PlayerId.Value).OrderBy(value => value, StringComparer.Ordinal),
            lines.Select(line => line.PlayerId.Value).OrderBy(value => value, StringComparer.Ordinal));

        // Minutes come from the rotation the depth chart built, so the two cannot disagree about who
        // was on the floor and for how long.
        Assert.All(lines, line => Assert.True(line.Minutes > 0));
        Assert.Equal(
            MinutesOnFloorForStarters(setup),
            lines.Count(line => line.Started));
    }

    [Fact]
    public void ARotationOfNobodyIsRefusedRatherThanPlayed()
    {
        var setup = MatchTestFixtures.Setup(seed: 5);

        var empty = setup with
        {
            Home = setup.Home with { Rotation = DepthChart.Empty(setup.Home.TeamId) },
        };

        var outcome = Engine.Play(empty);

        Assert.True(outcome.IsFailure);
        Assert.Equal(PossessionMatchEngine.EmptyRotationCode, Assert.Single(outcome.Errors).Code);
    }

    [Fact]
    public void AShortHandedTeamStillPlaysTheGame()
    {
        // Five available players is a legal, if unhappy, way to turn up. The depth chart reports the
        // breach of its minutes bound; the game still has to be played.
        var setup = MatchTestFixtures.Setup(seed: 77, homeSquadSize: 5);
        var outcome = Engine.Play(setup);

        Assert.True(outcome.IsSuccess, string.Join("; ", outcome.Errors.Select(error => error.Message)));
        Assert.Equal(5, outcome.Value.Result.BoxScore!.LinesFor(setup.Home.TeamId).Count);
    }

    [Fact]
    public void EveryInjuryIsInsideTheStatedBoundsAndBelongsToSomeoneWhoPlayed()
    {
        var setup = MatchTestFixtures.Setup(seed: 13);
        var rostered = setup.Home.Rotation.Slots.Select(slot => slot.PlayerId.Value)
            .Concat(setup.Away.Rotation.Slots.Select(slot => slot.PlayerId.Value))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var outcome in MatchTestFixtures.PlayMany(500))
        {
            foreach (var injury in outcome.Injuries)
            {
                Assert.InRange(injury.DaysOut, MatchModelBounds.MinimumInjuryDays, MatchModelBounds.MaximumInjuryDays);
                Assert.Contains(injury.PlayerId.Value, rostered);
                Assert.False(string.IsNullOrWhiteSpace(injury.Description));
            }
        }
    }

    [Fact]
    public void OvertimeIsPlayedByTheClosingFiveAndAddsItsMinutes()
    {
        // Found by search rather than asserted blind — and by the game's own overtime count, not by a
        // high score, which is what this test used to guess from.
        var overtime = Enumerable.Range(0, 2_000)
            .Select(seed => Engine.Play(MatchTestFixtures.Setup(seed)).Value)
            .First(outcome => outcome.Result.BoxScore!.OvertimePeriods > 0);

        var box = overtime.Result.BoxScore!;
        var lines = box.LinesFor(overtime.Result.HomeTeamId);

        Assert.Equal(240 + (25 * box.OvertimePeriods), lines.Sum(line => line.Minutes));
        Assert.All(lines, line => Assert.True(line.Minutes > 0));
    }

    [Fact]
    public void CreditedMinutesStayWithinOneOfTheDepthChart()
    {
        for (var seed = 0; seed < 500; seed++)
        {
            var setup = MatchTestFixtures.Setup(seed);
            var played = Engine.Play(setup).Value;
            if (played.Result.BoxScore!.OvertimePeriods > 0)
            {
                continue;
            }

            foreach (var slot in setup.Home.Rotation.Slots)
            {
                var credited = played.Result.BoxScore.Lines.Single(line => line.PlayerId == slot.PlayerId).Minutes;
                Assert.InRange(credited, slot.Minutes - 1, slot.Minutes + 1);
            }
        }
    }

    [Fact]
    public void OffensiveAndDefensiveReboundsAreARealSplitNotAConstantOnOneSide()
    {
        // Both categories are real rebound sources (a side's own misses, and its opponent's), so
        // across many games neither should sit at zero — a split that never varies would be the
        // merged total wearing two field names.
        var lines = MatchTestFixtures.PlayMany(300)
            .SelectMany(outcome => outcome.Result.BoxScore!.Lines)
            .ToList();

        Assert.Contains(lines, line => line.OffensiveRebounds > 0);
        Assert.Contains(lines, line => line.DefensiveRebounds > 0);
        Assert.All(lines, line => Assert.Equal(line.OffensiveRebounds + line.DefensiveRebounds, line.Rebounds));
    }

    private static int MinutesOnFloorForStarters(MatchSetup setup) =>
        setup.Home.Rotation.Slots.Count(slot => slot.IsStarter);

    [Fact]
    public void EveryBoxScoreIdentityHoldsAcrossTwoThousandVariedGames()
    {
        // A property check over seeds, rating gaps, rest and short squads. BoxScore.Create refuses a
        // game that breaks an identity, so a game that plays at all already satisfies them; the
        // rebound identity is recomputed here independently anyway, from the published box score.
        for (var seed = 0; seed < 2_000; seed++)
        {
            var setup = MatchTestFixtures.Setup(
                seed,
                homeRating: 50 + (seed % 41),
                awayRating: 90 - (seed * 7 % 41),
                homeRestDays: seed % 4,
                awayRestDays: (seed / 4) % 4,
                homeSquadSize: 5 + (seed % 6),
                awaySquadSize: 5 + ((seed / 6) % 6));

            var played = Engine.Play(setup);
            Assert.True(played.IsSuccess, $"seed {seed}: {string.Join("; ", played.Errors.Select(error => error.Message))}");

            var box = played.Value.Result.BoxScore!;
            foreach (var (team, opponent) in new[] { (box.Home, box.Away), (box.Away, box.Home) })
            {
                var own = box.Lines.Where(line => line.TeamId == team.TeamId).ToList();
                var theirs = box.Lines.Where(line => line.TeamId == opponent.TeamId).ToList();
                var liveMisses = own.Sum(line => line.FieldGoalsAttempted - line.FieldGoalsMade) + team.LiveFreeThrowMisses;
                var rebounds = own.Sum(line => line.OffensiveRebounds) + team.TeamOffensiveRebounds +
                    theirs.Sum(line => line.DefensiveRebounds) + opponent.TeamDefensiveRebounds;

                Assert.Equal(liveMisses, rebounds);
                Assert.True(own.Sum(line => line.Assists) <= own.Sum(line => line.FieldGoalsMade), $"seed {seed}: more assists than baskets.");
                Assert.True(own.Sum(line => line.Steals) <= theirs.Sum(line => line.Turnovers), $"seed {seed}: more steals than opponent turnovers.");
                Assert.True(
                    own.Sum(line => line.Blocks) <= theirs.Sum(line => line.FieldGoalsAttempted - line.FieldGoalsMade),
                    $"seed {seed}: more blocks than opponent misses.");
            }
        }
    }

    /// <summary>Every field of every line and both team lines: records print all their properties.</summary>
    private static string Describe(BoxScore boxScore) =>
        string.Join(
            "|",
            boxScore.Lines
                .OrderBy(line => line.PlayerId.Value, StringComparer.Ordinal)
                .Select(line => line.ToString())
                .Append(boxScore.Home.ToString())
                .Append(boxScore.Away.ToString()));
}
