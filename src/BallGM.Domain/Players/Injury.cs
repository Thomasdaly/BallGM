namespace BallGM.Domain.Players;

public sealed record Injury
{
    public Injury(string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        Description = description;
    }

    public string Description { get; }
}
