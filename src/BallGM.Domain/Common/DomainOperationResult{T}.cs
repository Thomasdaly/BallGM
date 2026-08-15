namespace BallGM.Domain.Common;

public sealed record DomainOperationResult<T>
{
    private readonly T? _value;

    private DomainOperationResult(bool isSuccess, T? value, IReadOnlyList<DomainError> errors)
    {
        IsSuccess = isSuccess;
        _value = value;
        Errors = errors;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public IReadOnlyList<DomainError> Errors { get; }

    public T Value =>
        IsSuccess
            ? _value!
            : throw new InvalidOperationException("Cannot access the value of a failed domain operation result.");

    public static DomainOperationResult<T> Success(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new DomainOperationResult<T>(true, value, Array.Empty<DomainError>());
    }

    public static DomainOperationResult<T> Failure(params DomainError[] errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (errors.Length == 0)
        {
            throw new ArgumentException("Failed domain operation results must include at least one error.", nameof(errors));
        }

        return new DomainOperationResult<T>(false, default, errors.ToArray());
    }
}
