namespace BallGM.Domain.Draft;

public sealed record ProspectId
{
    public ProspectId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
