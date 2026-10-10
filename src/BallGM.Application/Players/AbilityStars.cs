namespace BallGM.Application.Players;

/// <summary>
/// A coach's star rating of an ability figure, as a football-management game shows Current and
/// Potential Ability: half-stars from one to ten (half a star to five), so a GM reads "four and a half
/// stars" rather than a number he must learn the scale of. Every band is seven points wide: below 37
/// is half a star, 86 to 92 four and a half, 93 or above five.
/// </summary>
public static class AbilityStars
{
    public const int MaximumHalfStars = 10;

    public static int HalfStars(int ability) => Math.Clamp((ability - 23) / 7, 1, MaximumHalfStars);
}
