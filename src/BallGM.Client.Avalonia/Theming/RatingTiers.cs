using Avalonia.Media;

namespace BallGM.Client.Avalonia.Theming;

/// <summary>
/// The six rating tiers on the game's 0-100 Overall scale (spec 01 section 12): the number is always
/// shown, the tier name is the tooltip, the colour is the third signal, never the only one.
/// </summary>
public sealed record RatingTier(string Name, int Minimum, string BrushKey, Color DefaultColour, Color CvdColour)
{
    public string OnBrushKey => BrushKey.Replace("Brush", "OnBrush", StringComparison.Ordinal);
}

public static class RatingTiers
{
    public static readonly IReadOnlyList<RatingTier> All =
    [
        new("Elite", 90, "TierEliteBrush", Color.Parse("#F5C542"), Color.Parse("#FEE838")),
        new("All-Star", 80, "TierStarBrush", Color.Parse("#A77BFF"), Color.Parse("#DAC65B")),
        new("Starter", 70, "TierStarterBrush", Color.Parse("#58A6FF"), Color.Parse("#B4A76F")),
        new("Rotation", 60, "TierRotationBrush", Color.Parse("#2EC27E"), Color.Parse("#908B78")),
        new("Bench", 50, "TierBenchBrush", Color.Parse("#C5CDD8"), Color.Parse("#6F7073")),
        new("Fringe", int.MinValue, "TierFringeBrush", Color.Parse("#6B7686"), Color.Parse("#4F576C")),
    ];

    public static RatingTier For(int rating) => All.First(tier => rating >= tier.Minimum);
}
