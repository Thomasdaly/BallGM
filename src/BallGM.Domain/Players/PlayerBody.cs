namespace BallGM.Domain.Players;

/// <summary>
/// A player's measurements: height and wingspan in centimetres, weight in kilograms. Facts, not
/// skills — they never develop as attributes do — and the engine reads size from them rather than
/// from a 0–100 "height" score. A rating without one (a hand-built fixture) is read as average size.
/// </summary>
public sealed record PlayerBody
{
    public const int MinimumHeightCm = 150;
    public const int MaximumHeightCm = 240;
    public const int MinimumWeightKg = 55;
    public const int MaximumWeightKg = 170;

    /// <summary>A wingspan within this many centimetres of height either way; beyond it is a data error.</summary>
    public const int MaximumWingspanDifferenceCm = 30;

    public PlayerBody(int heightCm, int wingspanCm, int weightKg)
    {
        if (heightCm is < MinimumHeightCm or > MaximumHeightCm)
        {
            throw new ArgumentOutOfRangeException(nameof(heightCm), heightCm, $"Height must be between {MinimumHeightCm} and {MaximumHeightCm} cm.");
        }

        if (Math.Abs(wingspanCm - heightCm) > MaximumWingspanDifferenceCm)
        {
            throw new ArgumentOutOfRangeException(nameof(wingspanCm), wingspanCm, $"Wingspan must be within {MaximumWingspanDifferenceCm} cm of height.");
        }

        if (weightKg is < MinimumWeightKg or > MaximumWeightKg)
        {
            throw new ArgumentOutOfRangeException(nameof(weightKg), weightKg, $"Weight must be between {MinimumWeightKg} and {MaximumWeightKg} kg.");
        }

        HeightCm = heightCm;
        WingspanCm = wingspanCm;
        WeightKg = weightKg;
    }

    public int HeightCm { get; }

    public int WingspanCm { get; }

    public int WeightKg { get; }

    /// <summary>The league's average height: a player this tall has a height shape of zero.</summary>
    public const int AverageHeightCm = 200;

    /// <summary>Height-shape points per ten centimetres from <see cref="AverageHeightCm"/> — the scale the old 0–100 height attribute's gap from a player's level was on.</summary>
    public const int ShapePointsPerTenCentimetres = 25;

    /// <summary>How much taller than average he is, in height-shape points; uncapped — the engine caps its own couplings.</summary>
    public int HeightShape => (HeightCm - AverageHeightCm) * ShapePointsPerTenCentimetres / 10;

    /// <summary>The height a given shape describes: the inverse of <see cref="HeightShape"/>, for content stated on the old scale.</summary>
    public static int HeightCmForShape(int shape) => AverageHeightCm + (shape * 10 / ShapePointsPerTenCentimetres);

    /// <summary>Whether these measurements are valid, for content that must refuse rather than throw.</summary>
    public static bool IsValid(int heightCm, int wingspanCm, int weightKg) =>
        heightCm is >= MinimumHeightCm and <= MaximumHeightCm &&
        Math.Abs(wingspanCm - heightCm) <= MaximumWingspanDifferenceCm &&
        weightKg is >= MinimumWeightKg and <= MaximumWeightKg;
}
