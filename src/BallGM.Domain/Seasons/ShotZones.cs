namespace BallGM.Domain.Seasons;

/// <summary>Where on the floor a field goal was attempted from: two zones inside, one in between, two beyond the arc.</summary>
public enum ShotZone
{
    Rim,
    Paint,
    MidRange,
    Corner3,
    AboveBreak3,
}

/// <summary>
/// Field goals attempted and made from each of the five <see cref="ShotZone"/>s. Ten named counts
/// rather than an array, so a line prints and compares by value. On a player's line the zones add up
/// to his field goals, and the two three-point zones to his threes (checked by <see cref="PlayerStatLine"/>).
/// </summary>
public sealed record ShotZones(
    int RimAttempts,
    int RimMade,
    int PaintAttempts,
    int PaintMade,
    int MidRangeAttempts,
    int MidRangeMade,
    int Corner3Attempts,
    int Corner3Made,
    int AboveBreak3Attempts,
    int AboveBreak3Made)
{
    public static ShotZones None { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    public static IReadOnlyList<ShotZone> All { get; } = Enum.GetValues<ShotZone>();

    public static bool IsThree(ShotZone zone) => zone is ShotZone.Corner3 or ShotZone.AboveBreak3;

    public int Attempts(ShotZone zone) => zone switch
    {
        ShotZone.Rim => RimAttempts,
        ShotZone.Paint => PaintAttempts,
        ShotZone.MidRange => MidRangeAttempts,
        ShotZone.Corner3 => Corner3Attempts,
        _ => AboveBreak3Attempts,
    };

    public int Made(ShotZone zone) => zone switch
    {
        ShotZone.Rim => RimMade,
        ShotZone.Paint => PaintMade,
        ShotZone.MidRange => MidRangeMade,
        ShotZone.Corner3 => Corner3Made,
        _ => AboveBreak3Made,
    };

    public int TotalAttempts => RimAttempts + PaintAttempts + MidRangeAttempts + Corner3Attempts + AboveBreak3Attempts;

    public int TotalMade => RimMade + PaintMade + MidRangeMade + Corner3Made + AboveBreak3Made;

    public int ThreeAttempts => Corner3Attempts + AboveBreak3Attempts;

    public int ThreesMade => Corner3Made + AboveBreak3Made;

    /// <summary>Whether every count is non-negative and no zone has more makes than attempts.</summary>
    public bool IsConsistent => All.All(zone => Made(zone) >= 0 && Made(zone) <= Attempts(zone));

    public static ShotZones operator +(ShotZones left, ShotZones right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        return new ShotZones(
            left.RimAttempts + right.RimAttempts,
            left.RimMade + right.RimMade,
            left.PaintAttempts + right.PaintAttempts,
            left.PaintMade + right.PaintMade,
            left.MidRangeAttempts + right.MidRangeAttempts,
            left.MidRangeMade + right.MidRangeMade,
            left.Corner3Attempts + right.Corner3Attempts,
            left.Corner3Made + right.Corner3Made,
            left.AboveBreak3Attempts + right.AboveBreak3Attempts,
            left.AboveBreak3Made + right.AboveBreak3Made);
    }

    /// <summary>The ten counts in declaration order, for a save file.</summary>
    public IReadOnlyList<int> ToArray() =>
        [RimAttempts, RimMade, PaintAttempts, PaintMade, MidRangeAttempts, MidRangeMade, Corner3Attempts, Corner3Made, AboveBreak3Attempts, AboveBreak3Made];

    /// <summary>Back from <see cref="ToArray"/>; null unless the list is ten non-negative, consistent counts.</summary>
    public static ShotZones? FromArray(IReadOnlyList<int>? counts) =>
        counts is { Count: 10 } c && c.All(count => count >= 0) && new ShotZones(c[0], c[1], c[2], c[3], c[4], c[5], c[6], c[7], c[8], c[9]) is { IsConsistent: true } zones
            ? zones
            : null;
}
