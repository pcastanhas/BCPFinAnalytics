using BCPFinAnalytics.Common.Enums;
using BCPFinAnalytics.Common.Models;

namespace BCPFinAnalytics.Services.Helpers;

/// <summary>
/// Applies post-processing suppression rules to a completed list of ReportRows.
///
/// Called by report strategies after all rows are built but before
/// the ReportResult is returned. Modifies the row list in-place.
///
/// TWO SUPPRESSION RULES (driven by user options in ReportOptions):
///
/// 1. SuppressZeroAccounts (O= 'S' on RA/SM rows):
///    Remove Detail rows where ALL cells are null or zero.
///    Applied when the user checks "Suppress accounts with 0 value".
///
/// 2. SuppressInactiveSubtotals (O= 'Z' on SU rows):
///    Remove Total rows where ALL cells are null or zero.
///    Applied when the user checks "Suppress subtotals with no activity".
///
/// Pure helper — no database calls, no dependencies beyond the row list.
/// The row list passed in is the authoritative list; this method removes rows from it.
/// </summary>
public static class ReportPostProcessor
{
    /// <summary>
    /// Applies all configured suppression rules to the report row list.
    /// Modifies the list in-place by removing suppressed rows.
    /// </summary>
    /// <param name="rows">Mutable list of report rows — modified in-place.</param>
    /// <param name="options">User report options — drives which suppressions apply.</param>
    public static void ApplySuppression(List<ReportRow> rows, ReportOptions options)
    {
        if (options.SuppressZeroAccounts)
            SuppressZeroDetailRows(rows);

        if (options.SuppressInactiveSubtotals)
            SuppressZeroSubtotalRows(rows);

        // Always suppress orphaned section headers — headers with no
        // detail or total rows following before the next header/end.
        SuppressOrphanedHeaders(rows);
    }

    /// <summary>
    /// Removes SectionHeader rows that have no Detail or Total rows
    /// between them and the next SectionHeader (or end of list).
    /// This cleans up sections that were entirely suppressed by other rules.
    /// </summary>
    private static void SuppressOrphanedHeaders(List<ReportRow> rows)
    {
        var toRemove = new HashSet<int>();

        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].RowType != RowType.SectionHeader) continue;
            if (string.IsNullOrWhiteSpace(rows[i].AccountName)) continue; // blank spacer

            // Look ahead for any non-header, non-blank content before next header
            bool hasContent = false;
            for (int j = i + 1; j < rows.Count; j++)
            {
                var rt = rows[j].RowType;
                if (rt == RowType.SectionHeader && !string.IsNullOrWhiteSpace(rows[j].AccountName))
                    break; // hit next real header — stop looking
                if (rt == RowType.Detail || rt == RowType.Total || rt == RowType.GrandTotal)
                {
                    hasContent = true;
                    break;
                }
            }

            if (!hasContent)
                toRemove.Add(i);
        }

        // Remove in reverse order to preserve indices
        foreach (var idx in toRemove.OrderByDescending(x => x))
            rows.RemoveAt(idx);
    }

    /// <summary>
    /// Removes Detail rows where every cell value is null or zero.
    /// Skips header rows (SectionHeader, SubHeader) and total rows.
    /// </summary>
    private static void SuppressZeroDetailRows(List<ReportRow> rows)
    {
        rows.RemoveAll(row =>
            row.RowType == RowType.Detail
            && AllCellsZeroOrNull(row));
    }

    /// <summary>
    /// Removes Total and GrandTotal rows where every cell value is null or zero.
    /// </summary>
    private static void SuppressZeroSubtotalRows(List<ReportRow> rows)
    {
        rows.RemoveAll(row =>
            (row.RowType == RowType.Total || row.RowType == RowType.GrandTotal)
            && AllCellsZeroOrNull(row));
    }

    /// <summary>
    /// Returns true if all cells in the row are null or zero.
    /// A row with no cells at all is considered all-zero (e.g. header rows).
    /// </summary>
    private static bool AllCellsZeroOrNull(ReportRow row)
    {
        if (!row.Cells.Any()) return true;

        return row.Cells.Values.All(cv =>
            cv.Amount is null || cv.Amount == 0m);
    }
}
