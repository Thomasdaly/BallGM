namespace BallGM.Domain.Franchises;

public sealed record FranchiseId
{
    public FranchiseId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
