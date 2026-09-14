using BallGM.Domain.Common;
using BallGM.Domain.Contracts;
using BallGM.Domain.Draft;
using BallGM.Domain.Franchises;
using BallGM.Domain.Leagues;
using BallGM.Domain.Players;
using BallGM.Domain.Teams;
using BallGM.Rules.AI;
using BallGM.Rules.Configuration;

namespace BallGM.Rules.Tests.AI;

public sealed class DraftDecisionModelTests
{
    private static readonly Season CurrentSeason = new(2031);
    private static readonly RosterSizeLimits RosterLimits = new(2, 12);

    [Fact]
    public void PrefersAProspectAtANeededPositionOverAHigherRatedProspectElsewhere()
    {
        var (team, playersById) = BuildRosterNeedingCenter();
        var pool = new[]
        {
            BuildProspect("CENTER", Position.Center, trueOverall: 70),
            BuildProspect("GUARD", Position.PointGuard, trueOverall: 90),
        };

        var recommendation = DraftDecisionModel.Recommend(
            team.Id, team, playersById, pool, RosterLimits, ScoutingRules.None, CurrentSeason);

        Assert.NotNull(recommendation);
        Assert.Equal(new ProspectId("PROSPECT-CENTER"), recommendation!.ProspectId);
        Assert.Contains(recommendation.Rationale, finding => finding.RuleCode == "ai_draft_target.needs_match");
    }

    [Fact]
    public void FallsBackToBestScoutedOverallWhenNobodyLeftFillsANeed()
    {
        var (team, playersById) = BuildRosterNeedingCenter();
        var pool = new[]
        {
            BuildProspect("GUARD", Position.PointGuard, trueOverall: 90),
            BuildProspect("WING", Position.ShootingGuard, trueOverall: 85),
        };

        var recommendation = DraftDecisionModel.Recommend(
            team.Id, team, playersById, pool, RosterLimits, ScoutingRules.None, CurrentSeason);

        Assert.NotNull(recommendation);
        Assert.Equal(new ProspectId("PROSPECT-GUARD"), recommendation!.ProspectId);
        Assert.Contains(recommendation.Rationale, finding => finding.RuleCode == "ai_draft_target.best_available_no_need_match");
    }

    [Fact]
    public void ReadsTheScoutedMidpointRatherThanTheTrueRatingNearTheRatingCeiling()
    {
        var (team, playersById) = BuildRosterNeedingCenter();
        var pool = new[] { BuildProspect("CENTER", Position.Center, trueOverall: 98) };

        // Confidence 60% over a width-20 band: range clamps to 94-100 (the true 102 upper bound is
        // out of range), so the midpoint reads 97 — not the true 98 this model never looks at.
        var scoutingRules = ScoutingRules.Create(baseConfidence: 60, maxRangeWidth: 20).Value;

        var recommendation = DraftDecisionModel.Recommend(
            team.Id, team, playersById, pool, RosterLimits, scoutingRules, CurrentSeason);

        var finding = recommendation!.Rationale.Single(f => f.RuleCode == "ai_draft_target.scouted_quality");
        Assert.Contains("94-100", finding.Explanation);
        Assert.Contains("midpoint reading of 97", finding.Explanation);
        Assert.DoesNotContain("98", finding.Explanation);
    }

    [Fact]
    public void ReturnsNullWhenTheProspectPoolIsEmpty()
    {
        var (team, playersById) = BuildRosterNeedingCenter();

        var recommendation = DraftDecisionModel.Recommend(
            team.Id, team, playersById, [], RosterLimits, ScoutingRules.None, CurrentSeason);

        Assert.Null(recommendation);
    }

    private static (Team Team, IReadOnlyDictionary<PlayerId, Player> PlayersById) BuildRosterNeedingCenter()
    {
        var franchise = Franchise.Create(new FranchiseId("FRANCHISE-A"), "A Athletic").Value;

        // Every other position carries a backup so it reads as settled; centre is a lone weak starter
        // with nobody behind it, so it is the only stated need — the same lesson the trade- and
        // free-agent-targeting fixtures already learned: a five-player, one-per-position roster reads
        // as a need everywhere, which would defeat what this fixture exists to isolate.
        (Position Position, int Overall)[] roster =
        [
            (Position.PointGuard, 75),
            (Position.PointGuard, 65),
            (Position.ShootingGuard, 70),
            (Position.ShootingGuard, 65),
            (Position.SmallForward, 70),
            (Position.SmallForward, 65),
            (Position.PowerForward, 70),
            (Position.PowerForward, 65),
            (Position.Center, 40), // Weak starter, no backup: a stated need at centre.
        ];

        var players = new List<Player>();
        var playerIds = new List<PlayerId>();

        for (var index = 0; index < roster.Length; index++)
        {
            var (position, overall) = roster[index];
            var player = Player.Create(
                new PlayerId($"PLAYER-A-{index}"),
                $"A Player {index}",
                position,
                new PlayerRating(overall),
                new DateOnly(2000, 1, 1),
                seasonsOfService: 4).Value;

            players.Add(player);
            playerIds.Add(player.Id);
        }

        var team = Team.Create(new TeamId("TEAM-A"), franchise.Id, "A Team", RosterLimits, playerIds).Value;

        return (team, players.ToDictionary(player => player.Id));
    }

    private static Prospect BuildProspect(string key, Position position, int trueOverall) => Prospect.Create(
        new ProspectId($"PROSPECT-{key}"),
        $"Prospect {key}",
        position,
        new DateOnly(2013, 1, 1),
        new PlayerRating(trueOverall)).Value;
}
