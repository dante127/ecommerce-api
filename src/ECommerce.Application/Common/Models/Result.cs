namespace ECommerce.Application.Common.Models;

/// <summary>
/// Classifies an <see cref="Error"/> so the API layer can choose a status code without
/// inspecting the error code string.
/// </summary>
public enum ErrorType
{
    Validation,
    NotFound,
    Conflict,
    Unauthorized,
    Forbidden,
    BadRequest
}

public sealed record Error(string Code, string Message, ErrorType Type = ErrorType.BadRequest)
{
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.BadRequest);
    public static readonly Error NullValue = new("Error.NullValue", "The specified result value is null.", ErrorType.Validation);

    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);
    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);
    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);
    public static Error Unauthorized(string code, string message) => new(code, message, ErrorType.Unauthorized);
    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);
    public static Error BadRequest(string code, string message) => new(code, message, ErrorType.BadRequest);
}

public class Result
{
    protected Result(bool isSuccess, Error error)
    {
        if (isSuccess && error != Error.None)
            throw new InvalidOperationException("A successful result cannot have an error.");

        if (!isSuccess && error == Error.None)
            throw new InvalidOperationException("A failed result must have an error.");

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; }

    public static Result Success() => new(true, Error.None);
    public static Result Failure(Error error) => new(false, error);
}

public class Result<TValue> : Result
{
    private readonly TValue? _value;

    protected internal Result(TValue? value, bool isSuccess, Error error)
        : base(isSuccess, error)
    {
        _value = value;
    }

    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("The value of a failed result cannot be accessed.");

    public static Result<TValue> Success(TValue value) => new(value, true, Error.None);
    public static new Result<TValue> Failure(Error error) => new(default, false, error);

    public static implicit operator Result<TValue>(TValue? value) =>
        value is not null ? Success(value) : Failure(Error.NullValue);
}
