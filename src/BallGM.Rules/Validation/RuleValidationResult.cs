namespace BallGM.Rules.Validation;

public sealed record RuleViolation
{
    public RuleViolation(string code, string explanation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(explanation);

        Code = code;
        Explanation = explanation;
    }

    public string Code { get; }

    public string Explanation { get; }
}

public sealed record RuleValidationResult
{
    public RuleValidationResult(IReadOnlyList<RuleViolation> violations)
    {
        ArgumentNullException.ThrowIfNull(violations);
        Violations = violations.ToArray();
    }

    public IReadOnlyList<RuleViolation> Violations { get; }

    public bool IsValid => Violations.Count == 0;

    public static RuleValidationResult Valid { get; } = new(Array.Empty<RuleViolation>());

    public static RuleValidationResult Invalid(params RuleViolation[] violations)
    {
        ArgumentNullException.ThrowIfNull(violations);
        if (violations.Length == 0)
        {
            throw new ArgumentException("Invalid validation results must include at least one violation.", nameof(violations));
        }

        return new RuleValidationResult(violations);
    }
}
