namespace BCPFinAnalytics.Common.DTOs;

/// <summary>MRI Entity lookup item.</summary>
public class EntityDto
{
    public string EntityId { get; set; } = string.Empty;
    public string Name     { get; set; } = string.Empty;
    public string LedgCode { get; set; } = string.Empty;
}

/// <summary>MRI Project lookup item.</summary>
public class ProjectDto
{
    public string ProjId { get; set; } = string.Empty;
    public string Name   { get; set; } = string.Empty;
}

/// <summary>
/// Format dropdown item.
/// Sourced from: SELECT code, '('+RTRIM(code)+') '+RTRIM(name) FROM GUSR WHERE FINANTYP IN ('B','I')
/// </summary>
public class FormatDto
{
    public string Code        { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

/// <summary>
/// Budget dropdown item.
/// Sourced from: SELECT budtype, '('+RTRIM(budtype)+') '+RTRIM(descrptn) FROM GBTY
/// </summary>
public class BudgetDto
{
    public string BudType     { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

/// <summary>
/// Square Footage Type dropdown item.
/// Sourced from: SELECT SQFTTYPE, '('+RTRIM(sqfttype)+') '+RTRIM(descrptn) FROM SQTY
/// </summary>
public class SFTypeDto
{
    public string SFType      { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

/// <summary>
/// Basis multi-select item.
/// Sourced from: SELECT basis, DESCRPTN FROM BTYP WHERE basis &lt;&gt; 'B'
/// </summary>
public class BasisDto
{
    public string Basis       { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

/// <summary>Saved setting summary for dropdown display.</summary>
public class SavedSettingDto
{
    public int    SettingId   { get; set; }
    public string SettingName { get; set; } = string.Empty;
    public string UserId      { get; set; } = string.Empty;
    public bool   IsPublic    { get; set; }
}

/// <summary>
/// General Ledger dropdown item — carries all fields needed by AccountNumberFormatter.
/// AcctLgt: total digit length (GLCD.ACCTLGT) — for left-padding raw ACCTNUM.
/// AcctDsp: COBOL PICTURE mask (GLCD.ACCTDSP) — for display formatting.
/// ReArnAcct: retained earnings account (GLCD.REARNACC) — for unposted RE.
/// </summary>
public class GLDto
{
    public string LedgCode    { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int    AcctLgt     { get; set; }
    public string AcctDsp     { get; set; } = string.Empty;
    public string ReArnAcct   { get; set; } = string.Empty;
}
