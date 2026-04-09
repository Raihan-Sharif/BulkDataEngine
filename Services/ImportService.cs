using ClosedXML.Excel;
using System.Runtime.CompilerServices;
using System.Text;
using BulkDataEngine.Models;

namespace BulkDataEngine.Services
{
    public class ImportService : IImportService
    {
        private readonly ConverterSettings _settings;

        public ImportService(ConverterSettings settings) => _settings = settings;

        // â”€â”€ METADATA â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public async Task<(string[] ColumnNames, long EstimatedRows, string[][] PreviewRows)>
            GetMetadataAsync(string filePath, string fileExt, string csvDelimiter,
                             bool hasHeader, CancellationToken ct)
        {
            return fileExt is ".xlsx" or ".xls"
                ? GetExcelMetadata(filePath, hasHeader)
                : await GetCsvMetadataAsync(filePath, csvDelimiter, hasHeader, ct);
        }

        private (string[] ColumnNames, long EstimatedRows, string[][] PreviewRows)
            GetExcelMetadata(string filePath, bool hasHeader)
        {
            using var wb = new XLWorkbook(filePath);
            var ws = wb.Worksheet(1);
            int lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
            int lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;

            string[] cols;
            var preview = new List<string[]>();

            if (hasHeader && lastRow >= 1)
            {
                var hr = ws.Row(1);
                cols = Enumerable.Range(1, lastCol)
                    .Select(c => { var v = hr.Cell(c).GetString().Trim(); return string.IsNullOrEmpty(v) ? $"Column{c}" : v; })
                    .ToArray();
                for (int r = 2; r <= Math.Min(lastRow, 1 + _settings.PreviewRowCount); r++)
                    preview.Add(ReadExcelRow(ws.Row(r), lastCol));
            }
            else
            {
                cols = Enumerable.Range(1, lastCol).Select(c => $"Column{c}").ToArray();
                for (int r = 1; r <= Math.Min(lastRow, _settings.PreviewRowCount); r++)
                    preview.Add(ReadExcelRow(ws.Row(r), lastCol));
            }

            long dataRows = hasHeader ? Math.Max(0, lastRow - 1) : lastRow;
            return (cols, dataRows, preview.ToArray());
        }

        private static string[] ReadExcelRow(IXLRow row, int colCount) =>
            Enumerable.Range(1, colCount).Select(c => row.Cell(c).GetString()).ToArray();

        private async Task<(string[] ColumnNames, long EstimatedRows, string[][] PreviewRows)>
            GetCsvMetadataAsync(string filePath, string csvDelimiter, bool hasHeader, CancellationToken ct)
        {
            char delim = GetDelimChar(csvDelimiter);
            using var reader = new StreamReader(filePath, Encoding.UTF8, true, 65536);

            string[] cols = [];
            var preview = new List<string[]>();
            long lineCount = 0;
            string? line;

            while ((line = await reader.ReadLineAsync(ct)) != null)
            {
                lineCount++;
                var fields = ParseCsvLine(line, delim);

                if (lineCount == 1)
                {
                    cols = hasHeader
                        ? fields.Select((f, i) => string.IsNullOrWhiteSpace(f) ? $"Column{i + 1}" : f.Trim()).ToArray()
                        : Enumerable.Range(1, fields.Length).Select(i => $"Column{i}").ToArray();

                    if (!hasHeader && preview.Count < _settings.PreviewRowCount)
                        preview.Add(fields);
                }
                else if (preview.Count < _settings.PreviewRowCount)
                {
                    preview.Add(fields);
                }
            }

            long dataRows = hasHeader ? Math.Max(0, lineCount - 1) : lineCount;
            return (cols, dataRows, preview.ToArray());
        }

        // â”€â”€ ROW STREAMING â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public async IAsyncEnumerable<string[]> ReadRowsAsync(
            string filePath, string fileExt, string csvDelimiter, bool hasHeader,
            [EnumeratorCancellation] CancellationToken ct)
        {
            if (fileExt is ".xlsx" or ".xls")
            {
                await foreach (var row in ReadExcelRowsAsync(filePath, hasHeader, ct))
                    yield return row;
            }
            else
            {
                await foreach (var row in ReadCsvRowsAsync(filePath, csvDelimiter, hasHeader, ct))
                    yield return row;
            }
        }

        private async IAsyncEnumerable<string[]> ReadExcelRowsAsync(
            string filePath, bool hasHeader, [EnumeratorCancellation] CancellationToken ct)
        {
            using var wb = new XLWorkbook(filePath);
            var ws = wb.Worksheet(1);
            int lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
            bool firstRow = true;

            foreach (var row in ws.RowsUsed())
            {
                ct.ThrowIfCancellationRequested();
                if (firstRow) { firstRow = false; if (hasHeader) continue; }
                yield return ReadExcelRow(row, lastCol);
                await Task.Yield(); // yield control periodically
            }
        }

        private async IAsyncEnumerable<string[]> ReadCsvRowsAsync(
            string filePath, string csvDelimiter, bool hasHeader,
            [EnumeratorCancellation] CancellationToken ct)
        {
            char delim = GetDelimChar(csvDelimiter);
            using var reader = new StreamReader(filePath, Encoding.UTF8, true, 65536);
            bool firstLine = true;
            string? line;

            while ((line = await reader.ReadLineAsync(ct)) != null)
            {
                ct.ThrowIfCancellationRequested();
                if (firstLine) { firstLine = false; if (hasHeader) continue; }
                yield return ParseCsvLine(line, delim);
            }
        }

        // â”€â”€ RFC 4180 CSV PARSER (also strips ="value" Excel-safe wrapper) â”€â”€â”€â”€â”€
        public static string[] ParseCsvLine(string line, char delimiter)
        {
            var fields = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (!inQuotes)
                {
                    // Detect ="value" Excel-safe numeric format â€” drop the = and enter quotes
                    if (c == '=' && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        i++; // skip "
                        inQuotes = true;
                    }
                    else if (c == '"')
                    {
                        inQuotes = true;
                    }
                    else if (c == delimiter)
                    {
                        fields.Add(sb.ToString());
                        sb.Clear();
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
                else
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        { sb.Append('"'); i++; } // escaped ""
                        else
                            inQuotes = false;
                    }
                    else sb.Append(c);
                }
            }
            fields.Add(sb.ToString());
            return [.. fields];
        }

        private static char GetDelimChar(string delimiter) =>
            delimiter.Length > 0 ? delimiter[0] : ',';
    }
}
