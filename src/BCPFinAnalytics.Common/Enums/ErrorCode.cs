namespace BCPFinAnalytics.Common.Enums;

/// <summary>
/// Categorizes errors returned by the Services and DAL layers
/// via ServiceResult. Allows the UI to make decisions based on
/// error type without parsing error message strings.
/// </summary>
public enum ErrorCode
{
    None = 0,
    DatabaseError = 1,
    ValidationError = 2,
    ConfigError = 3,
    NotFound = 4,
    UnexpectedError = 5
}
