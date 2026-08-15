namespace BallGM.Domain.Leagues;

public sealed record LeagueId
{
    public LeagueId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
