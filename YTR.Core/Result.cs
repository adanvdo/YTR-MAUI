namespace YTR.Core;

/// <summary>
/// Represents the outcome of an operation that can fail in expected ways.
/// </summary>
public sealed class Result<T>
{
    public T? Value { get; }
    public string? Error { get; }
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;

    private Result(T value)
    {
        Value = value;
        IsSuccess = true;
    }

    private Result(string error)
    {
        Error = error;
        IsSuccess = false;
    }

    public static Result<T> Success(T value) => new(value);
    public static Result<T> Failure(string error) => new(error);

    public static implicit operator Result<T>(T value) => Success(value);
}

/// <summary>
/// Non-generic result for void operations.
/// </summary>
public sealed class Result
{
    public string? Error { get; }
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;

    private Result() => IsSuccess = true;
    private Result(string error)
    {
        Error = error;
        IsSuccess = false;
    }

    public static Result Success() => new();
    public static Result Failure(string error) => new(error);
}
