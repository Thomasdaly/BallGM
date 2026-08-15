namespace BallGM.Domain.Contracts;

public sealed record ContractId
{
    public ContractId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
