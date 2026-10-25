namespace BallGM.Rules.Seasons;

/// <summary>
/// Load management (<c>docs/franchise-realism.md</c> → F9): a coach manages an ageing star's minutes —
/// a little from <see cref="FirstAge"/>, more each year after — however good he still is. Without it a
/// 37-year-old who is still his team's best player plays the 38 minutes a 27-year-old would, and puts up
/// the season of his life.
/// </summary>
public static class VeteranMinutesPolicy
{
    /// <summary>The age from which minutes are managed.</summary>
    public const int FirstAge = 34;

    /// <summary>The minutes a game at <see cref="FirstAge"/>.</summary>
    public const int MinutesAtFirstAge = 34;

    /// <summary>Minutes a game fewer for each year past <see cref="FirstAge"/>.</summary>
    public const int MinutesLessPerYear = 2;

    /// <summary>The fewest a managed veteran is limited to: still a rotation player.</summary>
    public const int FloorMinutes = 24;

    /// <summary>The most minutes a game a player of this age is planned for, or 0 for no limit.</summary>
    public static int LimitFor(int age) =>
        age < FirstAge ? 0 : Math.Max(FloorMinutes, MinutesAtFirstAge - ((age - FirstAge) * MinutesLessPerYear));
}
