namespace BallGM.Domain.Players;

public sealed record PlayerId
{
    public PlayerId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
