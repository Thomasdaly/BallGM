namespace BallGM.Domain.Common;

public sealed record DomainOperationResult
{
    private DomainOperationResult(bool isSuccess, IReadOnlyList<DomainError> errors)
    {
        IsSuccess = isSuccess;
        Errors = errors.ToArray();
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public IReadOnlyList<DomainError> Errors { get; }

    public static DomainOperationResult Success { get; } = new(true, Array.Empty<DomainError>());

    public static DomainOperationResult Failure(params DomainError[] errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (errors.Length == 0)
        {
            throw new ArgumentException("Failed domain operation results must include at least one error.", nameof(errors));
        }

        return new DomainOperationResult(false, errors);
    }
}
