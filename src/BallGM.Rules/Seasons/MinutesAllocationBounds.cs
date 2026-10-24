using BallGM.Domain.Seasons;

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
    public const int RegulationMinutes = BoxScore.RegulationMinutes;

    /// <summary>How many of a team's players are on the floor at once.</summary>
    public const int PlayersOnFloor = BoxScore.PlayersOnFloor;

    /// <summary>The minutes one team has to distribute across one game.</summary>
    public const int TeamMinutesPerGame = RegulationMinutes * PlayersOnFloor;

    /// <summary>
    /// The most minutes one player is given in a normal game. Below the length of a game on purpose:
    /// a rotation that hands one player every minute is a rotation with no fatigue in it.
    /// </summary>
    public const int MaximumMinutesPerPlayer = 42;

    /// <summary>
    /// The most minutes the rotation plans for anyone, short-handed games aside: a star on a thin team
    /// plays deep into his thirties, but a coach still rests him. The game's own fatigue and fouls take
    /// it down from here, never up.
    /// </summary>
    public const int MaximumPlannedMinutes = 38;

    /// <summary>
    /// How many of the best players by Current Ability are considered for the starting five (with the
    /// best natural player at each position added when not among them).
    /// </summary>
    public const int StarterCandidates = 7;

    /// <summary>
    /// How many of a team's best players (by Current Ability) always start, wherever on the floor
    /// they can genuinely play: a coach does not bring his star off the bench because another player
    /// fits the position a little better — the competitor's "best player in the league is the sixth
    /// man" failure.
    /// </summary>
    public const int AlwaysStart = 4;

    /// <summary>
    /// The minutes a role is worth before quality and stamina move them, by place in the rotation: the
    /// five starters ranked among themselves by rating, then the bench in order. Sums to the game for a
    /// full rotation, which is the shape a real team's minutes take — a starter's thirty-odd, a sixth
    /// man's mid-twenties, the end of the bench in single figures.
    /// </summary>
    public static int RoleMinutes(int rank) => rank switch
    {
        0 => 33,
        1 => 31,
        2 => 30,
        3 => 28,
        4 => 27,
        5 => 25,
        6 => 22,
        7 => 18,
        8 => 14,
        _ => 12,
    };

    /// <summary>
    /// Tenths of a minute a point of Current Ability above (or below) the rotation's average adds (or
    /// takes away): the better player plays more than his role alone would give him.
    /// </summary>
    public const int TenthsPerRatingPoint = 2;

    /// <summary>
    /// Tenths of a minute a point of stamina above (or below) the player's own level adds (or takes
    /// away), capped either way at <see cref="MaximumStaminaTenths"/>.
    /// </summary>
    public const int TenthsPerStaminaPoint = 1;

    /// <inheritdoc cref="TenthsPerStaminaPoint"/>
    public const int MaximumStaminaTenths = 25;

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
