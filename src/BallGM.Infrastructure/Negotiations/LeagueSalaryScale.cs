using BallGM.Application.Leagues;
using BallGM.Rules.Negotiations;

namespace BallGM.Infrastructure.Negotiations;

/// <summary>The salary scale a league's asking prices rank against: every rostered player.</summary>
internal static class LeagueSalaryScale
{
    public static SalaryScale For(LeagueSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var rostered = snapshot.Teams.SelectMany(team => team.PlayerIds).ToHashSet();
        return SalaryScale.From(snapshot.Players.Where(player => rostered.Contains(player.Id)));
    }
}
