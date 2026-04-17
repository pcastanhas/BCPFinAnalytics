using BCPFinAnalytics.Common.Enums;
using BCPFinAnalytics.Common.Models;
using BCPFinAnalytics.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace BCPFinAnalytics.Services.Report;

/// <summary>
/// Provides reusable C# pivot logic for crosstab reports.
/// Takes flat normalized rows from the DAL and pivots them
/// into a ReportResult with dynamic columns.
/// SQL PIVOT is never used — all pivoting happens here.
/// Fully implemented in Phase 4.
/// </summary>
public class PivotService : IPivotService
{
    private readonly ILogger<PivotService> _logger;

    public PivotService(ILogger<PivotService> logger)
    {
        _logger = logger;
    }

    public ReportResult Pivot(
        IEnumerable<PivotFlatRow> flatRows,
        IEnumerable<(string ColumnId, string Header)> columnHeaders,
        ReportMetadata metadata)
    {
        _logger.LogDebug("PivotService.Pivot — building dynamic columns from {Count} flat rows", flatRows.Count());

        var rowList = flatRows.ToList();
        var columns = columnHeaders.Select(ch => new ReportColumn
        {
            ColumnId = ch.ColumnId,
            Header = ch.Header,
            DataType = ColumnDataType.Currency,
            RightAlign = true
        }).ToList();

        // Group flat rows by account
        var grouped = rowList
            .GroupBy(r => new { r.AccountCode, r.AccountName, r.AccountGroup, r.SortOrder })
            .OrderBy(g => g.Key.SortOrder)
            .ThenBy(g => g.Key.AccountCode);

        var reportRows = new List<ReportRow>();

        foreach (var group in grouped)
        {
            var row = new ReportRow
            {
                RowType = RowType.Detail,
                AccountCode = group.Key.AccountCode,
                AccountName = group.Key.AccountName,
                Cells = new Dictionary<string, CellValue>()
            };

            foreach (var col in columns)
            {
                var match = group.FirstOrDefault(r => r.ColumnId == col.ColumnId);
                row.Cells[col.ColumnId] = new CellValue(match?.Value);
            }

            reportRows.Add(row);
        }

        return new ReportResult
        {
            Columns = columns,
            Rows = reportRows,
            Metadata = metadata
        };
    }
}
