namespace BallGM.Domain.Teams;

public sealed record RosterSizeLimits
{
    public RosterSizeLimits(int minimumPlayers, int maximumPlayers)
    {
        if (minimumPlayers < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumPlayers), "Minimum roster size cannot be negative.");
        }

        if (maximumPlayers < minimumPlayers)
        {
            throw new ArgumentException("Maximum roster size must be greater than or equal to the minimum roster size.", nameof(maximumPlayers));
        }

        MinimumPlayers = minimumPlayers;
        MaximumPlayers = maximumPlayers;
    }

    public int MinimumPlayers { get; }

    public int MaximumPlayers { get; }
}
