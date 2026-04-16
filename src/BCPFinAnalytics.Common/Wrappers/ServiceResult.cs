using BCPFinAnalytics.Common.Enums;

namespace BCPFinAnalytics.Common.Wrappers;

/// <summary>
/// Generic result wrapper used by all Services and DAL methods.
///
/// Ensures no raw exceptions bubble up to the UI layer.
/// All service calls return ServiceResult&lt;T&gt; — the UI checks
/// IsSuccess and either renders Data or displays ErrorMessage.
///
/// Usage:
///   return ServiceResult&lt;List&lt;EntityDto&gt;&gt;.Success(entities);
///   return ServiceResult&lt;List&lt;EntityDto&gt;&gt;.Failure("DB error", ErrorCode.DatabaseError);
/// </summary>
public class ServiceResult<T>
{
    public bool IsSuccess { get; private set; }
    public T? Data { get; private set; }
    public string ErrorMessage { get; private set; } = string.Empty;
    public ErrorCode ErrorCode { get; private set; } = ErrorCode.None;

    private ServiceResult() { }

    /// <summary>Creates a successful result carrying the provided data.</summary>
    public static ServiceResult<T> Success(T data)
    {
        return new ServiceResult<T>
        {
            IsSuccess = true,
            Data = data,
            ErrorCode = ErrorCode.None
        };
    }

    /// <summary>Creates a failed result with an error message and optional error code.</summary>
    public static ServiceResult<T> Failure(
        string errorMessage,
        ErrorCode errorCode = ErrorCode.UnexpectedError)
    {
        return new ServiceResult<T>
        {
            IsSuccess = false,
            ErrorMessage = errorMessage,
            ErrorCode = errorCode
        };
    }

    /// <summary>
    /// Convenience — creates a failed result from an exception.
    /// The exception message is used; the full exception should be
    /// logged by the caller before calling this method.
    /// </summary>
    public static ServiceResult<T> FromException(
        Exception ex,
        ErrorCode errorCode = ErrorCode.UnexpectedError)
    {
        return new ServiceResult<T>
        {
            IsSuccess = false,
            ErrorMessage = $"An unexpected error occurred: {ex.Message}",
            ErrorCode = errorCode
        };
    }
}
