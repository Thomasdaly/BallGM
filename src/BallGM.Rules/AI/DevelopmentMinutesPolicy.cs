using BallGM.Domain.Seasons;

namespace BallGM.Rules.AI;

/// <summary>
/// Who a tanking team plays (<c>docs/franchise-realism.md</c> → F6): a team that won under
/// <see cref="TankingWinPercent"/> per cent last season stops playing to win and plays to develop — its
/// young players (<see cref="YouthAge"/> and under) move up the rotation, its veterans
/// (<see cref="VeteranAge"/> and over) down. It is how the worst teams in a real league get worse before
/// they get better, and why their young players' numbers look bigger than their ratings. A team playing
/// to win, and every team in a league's first season (no record to read), plays its best.
/// </summary>
public static class DevelopmentMinutesPolicy
{
    /// <summary>Below this share of wins last season, a team plays for the future.</summary>
    public const int TankingWinPercent = 40;

    /// <summary>The oldest player a tanking team plays for development.</summary>
    public const int YouthAge = 23;

    /// <summary>The youngest player a tanking team plays less.</summary>
    public const int VeteranAge = 30;

    /// <summary>Rating points of rotation priority a tanking team gives its young players.</summary>
    public const int YouthPriority = 8;

    /// <summary>Rating points of rotation priority a tanking team takes from its veterans.</summary>
    public const int VeteranPriority = -6;

    public static int PriorityFor(TeamRecord? lastSeason, int age)
    {
        if (lastSeason is not { Games: > 0 } record || record.Wins * 100 >= TankingWinPercent * record.Games)
        {
            return 0;
        }

        return age <= YouthAge ? YouthPriority : age >= VeteranAge ? VeteranPriority : 0;
    }
}
