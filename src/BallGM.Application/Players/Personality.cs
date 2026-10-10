using BallGM.Domain.Players;

namespace BallGM.Application.Players;

/// <summary>
/// The one-line read a scout gives of a player's hidden attributes, as a football-management game's
/// "personality": derived, never stored, and the only way the hidden layer reaches a screen.
/// The first description that fits wins, most telling first.
/// </summary>
public static class Personality
{
    public static string Describe(PlayerRating rating)
    {
        ArgumentNullException.ThrowIfNull(rating);
        var professionalism = rating.Hidden(HiddenAttribute.Professionalism);
        var ambition = rating.Hidden(HiddenAttribute.Ambition);
        var temperament = rating.Hidden(HiddenAttribute.Temperament);
        var loyalty = rating.Hidden(HiddenAttribute.Loyalty);
        var bigGames = rating.Hidden(HiddenAttribute.BigGames);

        return (professionalism, ambition, temperament, loyalty, bigGames) switch
        {
            ( >= 80, >= 70, _, _, _) => "Model professional",
            ( >= 80, _, _, _, _) => "Professional",
            (_, _, <= 20, _, _) => "Volatile",
            ( <= 25, _, _, _, _) => "Casual",
            (_, >= 80, _, _, _) => "Driven",
            (_, _, _, _, >= 80) => "Big-game player",
            (_, _, _, >= 80, _) => "Loyal",
            (_, _, >= 75, _, _) => "Level-headed",
            _ => "Balanced",
        };
    }
}
