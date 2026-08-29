namespace BallGM.Rules.Seasons;

/// <summary>
/// The bounds minutes allocation works inside, as named constants with their own tests rather than
/// numbers buried in the allocator.
/// <para>
/// These are not ruleset fields. A league may configure how many games it plays and how its
/// postseason is drawn; how long a basketball game is, and how many players are on the floor, are
/// properties of the sport rather than of one league's agreement. Making them configuration would
/// invite a data pack to state a 90-minute game and then discover that every bound derived from it
/// silently stopped holding.
/// </para>
/// <para>
/// The relationships between them are asserted in <c>MinutesAllocationBoundsTests</c>, which is the
/// point of naming them: a constant nobody checks is a magic number with a better name.
/// </para>
/// </summary>
public static class MinutesAllocationBounds
{
    /// <summary>Length of a game.</summary>
    public const int RegulationMinutes = 48;

    /// <summary>How many of a team's players are on the floor at once.</summary>
    public const int PlayersOnFloor = 5;

    /// <summary>The minutes one team has to distribute across one game.</summary>
    public const int TeamMinutesPerGame = RegulationMinutes * PlayersOnFloor;

    /// <summary>
    /// The most minutes one player is given in a normal game. Below the length of a game on purpose:
    /// a rotation that hands one player every minute is a rotation with no fatigue in it.
    /// </summary>
    public const int MaximumMinutesPerPlayer = 42;

    /// <summary>
    /// The fewest minutes a player kept in the rotation is given. A player worth playing at all is
    /// worth more than a token appearance, and a floor stops the allocation from producing a
    /// rotation of ten in which four of them play two minutes each.
    /// </summary>
    public const int MinimumRotationMinutes = 6;

    /// <summary>The most players a team uses in one game.</summary>
    public const int MaximumRotationSize = 10;

    /// <summary>
    /// The smallest rotation that can cover a game without anybody exceeding
    /// <see cref="MaximumMinutesPerPlayer"/>. A team with fewer available players than this is
    /// short-handed, and the allocator says so rather than quietly breaking its own bound.
    /// </summary>
    public static int MinimumRotationWithinBounds =>
        (TeamMinutesPerGame + MaximumMinutesPerPlayer - 1) / MaximumMinutesPerPlayer;
}
