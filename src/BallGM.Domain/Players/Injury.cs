namespace BallGM.Domain.Players;

/// <summary>
/// An injury a player carries outside a season's own spells: one a data pack states, or one that ran
/// past the end of the season it happened in. <see cref="DaysRemaining"/> is how long he is still out
/// (null when a pack does not say); the next season turns it into a spell from its first day.
/// </summary>
public sealed record Injury
{
    public Injury(string description, int? daysRemaining = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        if (daysRemaining is < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(daysRemaining), daysRemaining, "An injury still being carried has at least a day left.");
        }

        Description = description;
        DaysRemaining = daysRemaining;
    }

    public string Description { get; }

    public int? DaysRemaining { get; }
}
