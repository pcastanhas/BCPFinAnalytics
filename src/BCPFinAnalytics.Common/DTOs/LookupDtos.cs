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

/// <summary>
/// General Ledger dropdown item.
/// Sourced from GLCD — carries all fields needed by AccountNumberFormatter.
///
/// AcctLgt: total digit length of the account number (GLCD.ACCTLGT).
///          Used to left-pad raw ACCTNUM before applying the display mask.
///
/// AcctDsp: COBOL-style PICTURE mask (GLCD.ACCTDSP).
///          Each char is '9' (digit placeholder) or '-'/'.' (literal separator).
///          Used by AccountNumberFormatter.Format() to produce the display string.
/// </summary>
public record GLDto(
    string LedgCode,
    string DisplayName,
    int    AcctLgt,
    string AcctDsp,
    string ReArnAcct);   // GLCD.REARNACC — retained earnings account for unposted RE
