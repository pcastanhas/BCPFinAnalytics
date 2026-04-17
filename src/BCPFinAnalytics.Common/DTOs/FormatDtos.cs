namespace BCPFinAnalytics.Common.DTOs;

/// <summary>
/// Raw GUSR row — format header.
/// </summary>
public class FormatHeaderDto
{
    public string Code     { get; set; } = string.Empty;
    public string Name     { get; set; } = string.Empty;
    public string LedgCode { get; set; } = string.Empty;
    public string FinanTyp { get; set; } = string.Empty;
}

/// <summary>
/// Raw MRIGLRW row — one line in a format definition.
/// All fields are returned as-is from the database — no parsing applied.
/// </summary>
public class FormatRowDto
{
    public string  FormatId { get; set; } = string.Empty;
    public int     SortOrd  { get; set; }
    public string  Type     { get; set; } = string.Empty;
    public int     SubtotId { get; set; }
    public string? DebCred  { get; set; }
    public string? LineDef  { get; set; }
}

/// <summary>
/// One BEGACCT/ENDACCT pair from GARR — a single account range within a named group.
/// </summary>
public record AccountRangeDto(string BegAcct, string EndAcct);
