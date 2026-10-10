using BallGM.Domain.Leagues;
using BallGM.Domain.Players;
using BallGM.Domain.Seasons;
using BallGM.Domain.Teams;
using BallGM.Rules.Seasons;
using BallGM.Simulation.Seasons;

namespace BallGM.Simulation.Tests;

/// <summary>
/// The engine reads a player's shape — an attribute against their own Overall — not just their level.
/// Each coupling is checked between two sides of identical Overall, so only the shape differs.
/// </summary>
public sealed class AttributeCouplingTests
{
    private static readonly PossessionMatchEngine Engine = new();

    private static readonly Lazy<(int HomeTurnovers, int AwaySteals, int AwayBlocks)> Neutral = new(() => Totals(Rating(), Rating()));

    [Fact]
    public void BetterPassersForTheirLevelTurnTheBallOverLess()
    {
        // Both sides are Overall 70. One has its passing raised 15 and its strength lowered 15.
        var passers = Totals(home: Rating(passing: +15, strength: -15), away: Rating()).HomeTurnovers;

        Assert.True(passers < Neutral.Value.HomeTurnovers * 0.95, $"passers {passers} vs neutral {Neutral.Value.HomeTurnovers}");
    }

    [Fact]
    public void AQuickerDefenceForcesMoreTurnoversAndStealsMore()
    {
        var quickDefence = Totals(home: Rating(), away: Rating(speed: +10, lateral: +10, height: -10, strength: -10));

        Assert.True(quickDefence.HomeTurnovers > Neutral.Value.HomeTurnovers * 1.03, $"{quickDefence.HomeTurnovers} vs {Neutral.Value.HomeTurnovers}");
        Assert.True(quickDefence.AwaySteals > Neutral.Value.AwaySteals, $"{quickDefence.AwaySteals} vs {Neutral.Value.AwaySteals}");
    }

    [Fact]
    public void ATallerDefenceBlocksMoreShots()
    {
        // Height up, strength down: nothing in the engine reads strength's shape, so only height moves.
        var tallDefence = Totals(home: Rating(), away: Rating(height: +15, strength: -15));

        Assert.True(tallDefence.AwayBlocks > Neutral.Value.AwayBlocks * 1.05, $"{tallDefence.AwayBlocks} vs {Neutral.Value.AwayBlocks}");
    }

    [Fact]
    public void SlowerFeetForTheirLevelFoulMore()
    {
        // Speed and lateral quickness down, strength up: only the quickness shape moves.
        var slow = Fouls(Rating(speed: -10, lateral: -10, strength: +20));
        var neutral = Fouls(Rating());

        Assert.True(slow > neutral, $"{slow} vs {neutral}");
    }

    [Fact]
    public void TallerShootersForTheirLevelTakeMoreShotsAtTheRimAndFewerThrees()
    {
        (double Rim, double Three) Diet(PlayerRating rating)
        {
            var zones = Enumerable.Range(0, 400)
                .Select(seed => MatchTestFixtures.Setup(seed, rating, Rating()))
                .Select(setup => (setup, box: Engine.Play(setup).Value.Result.BoxScore!))
                .SelectMany(game => game.box.LinesFor(game.setup.Home.TeamId))
                .Aggregate(ShotZones.None, (total, line) => total + line.Zones!);
            return ((double)zones.RimAttempts / zones.TotalAttempts, (double)zones.ThreeAttempts / zones.TotalAttempts);
        }

        var tall = Diet(Rating(height: +15, strength: -15));
        var neutral = Diet(Rating());

        Assert.True(tall.Rim > neutral.Rim + 0.05, $"rim {tall.Rim:F3} vs {neutral.Rim:F3}");
        Assert.True(tall.Three < neutral.Three - 0.05, $"three {tall.Three:F3} vs {neutral.Three:F3}");
    }

    /// <summary>The away side's personal fouls against a neutral home side: fouls fall on the defence's players.</summary>
    private static int Fouls(PlayerRating away) => Enumerable.Range(0, 600)
        .Select(seed => MatchTestFixtures.Setup(seed, Rating(), away))
        .Sum(setup => Engine.Play(setup).Value.Result.BoxScore!.LinesFor(setup.Away.TeamId).Sum(line => line.PersonalFouls));

    private static (int HomeTurnovers, int AwaySteals, int AwayBlocks) Totals(PlayerRating home, PlayerRating away)
    {
        var turnovers = 0;
        var steals = 0;
        var blocks = 0;
        for (var seed = 0; seed < 600; seed++)
        {
            var setup = MatchTestFixtures.Setup(seed, home, away);
            var box = Engine.Play(setup).Value.Result.BoxScore!;
            turnovers += box.LinesFor(setup.Home.TeamId).Sum(line => line.Turnovers);
            steals += box.LinesFor(setup.Away.TeamId).Sum(line => line.Steals);
            blocks += box.LinesFor(setup.Away.TeamId).Sum(line => line.Blocks);
        }

        return (turnovers, steals, blocks);
    }

    /// <summary>A rating of Overall 70 with the given shape: the deltas must sum to zero to keep the level.</summary>
    private static PlayerRating Rating(int height = 0, int speed = 0, int strength = 0, int passing = 0, int lateral = 0) =>
        new(70 + height, 70 + speed, 70 + strength, 70 + passing, 70 + lateral);
}
