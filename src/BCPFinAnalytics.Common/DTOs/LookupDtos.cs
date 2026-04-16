namespace BCPFinAnalytics.Common.DTOs;

/// <summary>MRI Entity lookup item.</summary>
public record EntityDto(string EntityId, string Name);

/// <summary>MRI Project lookup item.</summary>
public record ProjectDto(string ProjId, string Name);

/// <summary>
/// Format dropdown item.
/// Sourced from: SELECT code, '('+RTRIM(code)+') '+RTRIM(name) FROM GUSR WHERE FINANTYP IN ('B','I')
/// Also used as the Report Type dropdown source.
/// </summary>
public record FormatDto(string Code, string DisplayName);

/// <summary>
/// Budget dropdown item.
/// Sourced from: SELECT budtype, '('+RTRIM(budtype)+') '+RTRIM(descrptn) FROM GBTY
/// </summary>
public record BudgetDto(string BudType, string DisplayName);

/// <summary>
/// Square Footage Type dropdown item.
/// Sourced from: SELECT SQFTTYPE, '('+RTRIM(sqfttype)+') '+RTRIM(descrptn) FROM SQTY
/// </summary>
public record SFTypeDto(string SFType, string DisplayName);

/// <summary>
/// Basis multi-select item.
/// Sourced from: SELECT basis, DESCRPTN FROM BTYP WHERE basis &lt;&gt; 'B'
/// </summary>
public record BasisDto(string Basis, string DisplayName);

/// <summary>Saved setting summary for dropdown display.</summary>
public record SavedSettingDto(
    int SettingId,
    string SettingName,
    string UserId,
    bool IsPublic);
