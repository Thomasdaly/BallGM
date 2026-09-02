namespace BallGM.Domain.Draft;

public sealed record DraftClassId
{
    public DraftClassId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
