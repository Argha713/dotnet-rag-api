using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace RagApi.Infrastructure.DocumentProcessing;

// Argha - 2026-03-22 - #75 - Static helpers for XLSX and CSV text extraction; used by DocumentProcessor
internal static class ExcelCsvParser
{
    // Argha - 2026-03-22 - #75 - Opens an XLSX stream and converts each data row to "Header1: Value | Header2: Value" format
    public static async Task<string> ExtractFromXlsxAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        // SpreadsheetDocument.Open is sync; wrap in Task.Run to avoid blocking the caller's thread
        return await Task.Run(() =>
        {
            using var doc = SpreadsheetDocument.Open(stream, isEditable: false);
            var workbookPart = doc.WorkbookPart
                ?? throw new InvalidOperationException("XLSX has no workbook part");

            var sb = new StringBuilder();

            foreach (var sheet in workbookPart.Workbook.Sheets?.Cast<Sheet>() ?? [])
            {
                var id = sheet.Id?.Value;
                if (id == null) continue;

                var worksheetPart = (WorksheetPart)workbookPart.GetPartById(id);
                var sheetData = worksheetPart.Worksheet.GetFirstChild<SheetData>();
                if (sheetData == null) continue;

                var rows = sheetData.Elements<Row>().ToList();
                if (rows.Count == 0) continue;

                // Argha - 2026-03-22 - #75 - First row is the header row
                var headers = rows[0].Elements<Cell>()
                    .Select(c => GetCellValue(workbookPart, c))
                    .ToList();

                foreach (var row in rows.Skip(1))
                {
                    var cells = row.Elements<Cell>().ToList();
                    var parts = new List<string>();

                    for (int i = 0; i < headers.Count; i++)
                    {
                        var header = headers[i];
                        var value = i < cells.Count ? GetCellValue(workbookPart, cells[i]) : string.Empty;
                        if (!string.IsNullOrWhiteSpace(header))
                            parts.Add($"{header}: {value}");
                    }

                    if (parts.Count > 0)
                        sb.AppendLine(string.Join(" | ", parts));
                }
            }

            return sb.ToString();
        }, cancellationToken);
    }

    // Argha - 2026-03-22 - #75 - Reads a CSV stream and converts each data row to "Header1: Value | Header2: Value" format
    public static async Task<string> ExtractFromCsvAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(stream, leaveOpen: true);
        var sb = new StringBuilder();

        var headerLine = await reader.ReadLineAsync(cancellationToken);
        if (headerLine == null) return string.Empty;

        var headers = ParseCsvLine(headerLine);

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
        {
            var values = ParseCsvLine(line);
            var parts = new List<string>();

            for (int i = 0; i < headers.Count; i++)
            {
                var header = headers[i];
                var value = i < values.Count ? values[i] : string.Empty;
                if (!string.IsNullOrWhiteSpace(header))
                    parts.Add($"{header}: {value}");
            }

            if (parts.Count > 0)
                sb.AppendLine(string.Join(" | ", parts));
        }

        return sb.ToString();
    }

    // Argha - 2026-03-22 - #75 - Resolve cell value accounting for shared string table
    private static string GetCellValue(WorkbookPart workbookPart, Cell cell)
    {
        var value = cell.CellValue?.Text ?? string.Empty;

        // Argha - 2026-03-22 - #75 - SharedString type means value is an index into the shared string table
        if (cell.DataType?.Value == CellValues.SharedString)
        {
            var sst = workbookPart.SharedStringTablePart?.SharedStringTable;
            if (sst != null && int.TryParse(value, out var idx))
            {
                value = sst.Elements<SharedStringItem>().ElementAtOrDefault(idx)?.InnerText ?? value;
            }
        }

        return value;
    }

    // Argha - 2026-03-22 - #75 - Naive CSV line parser; handles quoted fields with embedded commas
    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            var ch = line[i];

            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    // Argha - 2026-03-22 - #75 - Escaped quote inside quoted field
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (ch == ',' && !inQuotes)
            {
                fields.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        fields.Add(current.ToString().Trim());
        return fields;
    }
}
