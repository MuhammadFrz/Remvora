namespace Remvora.Core.Domain.Results;

/// <summary>
/// Structured error codes for predictable, non-exception-driven control flow.
/// </summary>
public enum ErrorCode
{
    None = 0,
    NotFound = 1,
    AccessDenied = 2,
    InUse = 3,
    Locked = 4,
    Timeout = 5,
    Cancelled = 6,
    ValidationFailed = 7,
    ProtectedTarget = 8,
    RebootRequired = 9,
    WorkerUnavailable = 10,
    WorkerRejected = 11,
    BackupFailed = 12,
    UninstallFailed = 13,
    UninstallUnknown = 14,
    PartialSuccess = 15,
    OperationFailed = 16,
    InvalidPath = 17,
    InvalidRegistryKey = 18,
    ProtocolViolation = 19
}

/// <summary>
/// Represents a structured error with an error code, message, and optional details.
/// </summary>
public sealed record OperationError(
    ErrorCode Code,
    string Message,
    string? Target = null,
    string? Details = null);

/// <summary>
/// Functional result container representing either a successful value or a structured error.
/// </summary>
public readonly struct OperationResult<T>
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public T? Value { get; }
    public OperationError? Error { get; }

    internal OperationResult(T value)
    {
        IsSuccess = true;
        Value = value;
        Error = null;
    }

    internal OperationResult(OperationError error)
    {
        IsSuccess = false;
        Value = default;
        Error = error ?? throw new ArgumentNullException(nameof(error));
    }

    public static implicit operator OperationResult<T>(T value) => new(value);
    public static implicit operator OperationResult<T>(OperationError error) => new(error);
}

/// <summary>
/// Functional result container for operations that produce no return value, plus factory methods.
/// </summary>
public readonly struct OperationResult
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public OperationError? Error { get; }

    private OperationResult(bool isSuccess, OperationError? error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public static OperationResult Success() => new(true, null);

    public static OperationResult Failure(ErrorCode code, string message, string? target = null, string? details = null)
        => new(false, new OperationError(code, message, target, details));

    public static OperationResult Failure(OperationError error) => new(false, error);

    public static OperationResult<T> Success<T>(T value) => new(value);

    public static OperationResult<T> Failure<T>(ErrorCode code, string message, string? target = null, string? details = null)
        => new(new OperationError(code, message, target, details));

    public static OperationResult<T> Failure<T>(OperationError error) => new(error);

    public static implicit operator OperationResult(OperationError error) => Failure(error);
}
