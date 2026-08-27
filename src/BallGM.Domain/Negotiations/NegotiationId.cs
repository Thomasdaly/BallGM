namespace BallGM.Domain.Negotiations;

public sealed record NegotiationId
{
    public NegotiationId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
