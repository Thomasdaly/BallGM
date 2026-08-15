namespace BallGM.Domain.Players;

/// <summary>
/// Minimal single-number skill rating proving the domain concept out for Milestone 1.
/// Expected to grow into a multi-attribute breakdown later without breaking callers,
/// since it is already its own value object rather than a bare int on <see cref="Player"/>.
/// </summary>
public sealed record PlayerRating
{
    public const int MinimumOverall = 0;
    public const int MaximumOverall = 100;

    public PlayerRating(int overall)
    {
        if (overall < MinimumOverall || overall > MaximumOverall)
        {
            throw new ArgumentOutOfRangeException(
                nameof(overall),
                overall,
                $"Overall rating must be between {MinimumOverall} and {MaximumOverall}.");
        }

        Overall = overall;
    }

    public int Overall { get; }
}
