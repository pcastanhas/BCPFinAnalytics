namespace BCPFinAnalytics.Common.DTOs;

/// <summary>
/// A single journal entry line returned by the GL Detail drill-down query.
/// Sourced from JOURNAL (open periods) UNION ALL GHIS (closed periods).
///
/// Column names match the query exactly — Dapper maps by column alias.
/// </summary>
public record GlDetailRow(
    string Period,
    string Ref,
    string Source,
    string Basis,
    string EntityId,
    string AcctNum,
    string Department,
    string Item,
    string JobCode,
    DateTime? EntrDate,
    string Descrpn,
    decimal Amt);
