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
        // Both sides are Overall 70. One has its handle, passing and vision raised 15.
        var passers = Totals(home: Rating(playmaking: +15), away: Rating()).HomeTurnovers;

        Assert.True(passers < Neutral.Value.HomeTurnovers * 0.95, $"passers {passers} vs neutral {Neutral.Value.HomeTurnovers}");
    }

    [Fact]
    public void AQuickerDefenceForcesMoreTurnoversAndStealsMore()
    {
        var quickDefence = Totals(home: Rating(), away: Rating(quickness: +10, heightCm: 196));

        Assert.True(quickDefence.HomeTurnovers > Neutral.Value.HomeTurnovers * 1.03, $"{quickDefence.HomeTurnovers} vs {Neutral.Value.HomeTurnovers}");
        Assert.True(quickDefence.AwaySteals > Neutral.Value.AwaySteals, $"{quickDefence.AwaySteals} vs {Neutral.Value.AwaySteals}");
    }

    [Fact]
    public void ATallerDefenceBlocksMoreShots()
    {
        // Six centimetres taller than average, nothing else changed.
        var tallDefence = Totals(home: Rating(), away: Rating(heightCm: 206));

        Assert.True(tallDefence.AwayBlocks > Neutral.Value.AwayBlocks * 1.05, $"{tallDefence.AwayBlocks} vs {Neutral.Value.AwayBlocks}");
    }

    [Fact]
    public void SlowerFeetForTheirLevelFoulMore()
    {
        // Speed, first step and lateral quickness down; Overall held by the mental attributes.
        var slow = Fouls(Rating(quickness: -10));
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

        var tall = Diet(Rating(heightCm: 206));
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

    /// <summary>
    /// A sheet of Overall 70 with the given shape: ball skills and feet moved as the engine reads them,
    /// a body of the given height (none is average size), and the mental attributes — which the engine
    /// does not read yet — shifted to hold Current Ability at exactly 70.
    /// </summary>
    private static PlayerRating Rating(int playmaking = 0, int quickness = 0, int? heightCm = null)
    {
        var rating = new PlayerRating(70);
        foreach (var attribute in new[] { PlayerAttribute.BallHandling, PlayerAttribute.Passing, PlayerAttribute.Vision })
        {
            rating = rating.With(attribute, 70 + playmaking);
        }

        foreach (var attribute in new[] { PlayerAttribute.Speed, PlayerAttribute.FirstStep, PlayerAttribute.LateralQuickness })
        {
            rating = rating.With(attribute, 70 + quickness);
        }

        var mental = PlayerAttributes.All.Where(attribute => PlayerAttributes.GroupOf(attribute) == AttributeGroup.Mental).ToList();
        for (var step = 0; step < 100 && rating.Overall != 70; step++)
        {
            var direction = Math.Sign(70 - rating.Overall);
            rating = rating.With(mental[step % mental.Count], rating[mental[step % mental.Count]] + direction);
        }

        Assert.Equal(70, rating.Overall);
        return heightCm is int height ? rating.WithBody(new PlayerBody(height, height + 5, 100)) : rating;
    }
}
