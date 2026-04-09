using ClosedXML.Excel;
using System.Text;
using BulkDataEngine.Models;

namespace BulkDataEngine.Services
{
    public class ConversionService : IConversionService
    {
        private readonly ConverterSettings _settings;
        private readonly ILogger<ConversionService> _logger;

        public ConversionService(ConverterSettings settings, ILogger<ConversionService> logger)
        {
            _settings = settings;
            _logger = logger;
        }

        public async Task ConvertAsync(ConversionJob job, string delimiter, bool hasHeader, CancellationToken cancellationToken)
        {
            try
            {
                job.Status = JobStatus.Processing;
                job.StatusMessage = "Reading file...";
                job.Delimiter = delimiter;
                job.HasHeader = hasHeader;

                if (string.IsNullOrEmpty(job.InputFilePath) || !File.Exists(job.InputFilePath))
                    throw new FileNotFoundException("Input file not found.");

                job.TotalBytes = new FileInfo(job.InputFilePath).Length;

                if (job.OutputFormat.Equals("xlsx", StringComparison.OrdinalIgnoreCase))
                    await ConvertToExcelAsync(job, delimiter, hasHeader, cancellationToken);
                else
                    await ConvertToCsvAsync(job, delimiter, hasHeader, cancellationToken);

                // Extract preview metadata from the input file
                await ExtractPreviewAsync(job, delimiter, hasHeader, cancellationToken);

                job.Status = JobStatus.Completed;
                job.ProgressPercent = 100;
                job.CompletedAt = DateTime.UtcNow;
                job.OutputFileSizeBytes = File.Exists(job.OutputFilePath) ? new FileInfo(job.OutputFilePath!).Length : 0;
                job.StatusMessage = $"Conversion complete. {job.ProcessedLines:N0} rows, {job.ColumnCount} columns.";
            }
            catch (OperationCanceledException)
            {
                job.Status = JobStatus.Cancelled;
                job.StatusMessage = "Conversion cancelled.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Conversion failed for job {JobId}", job.JobId);
                job.Status = JobStatus.Failed;
                job.ErrorMessage = ex.Message;
                job.StatusMessage = $"Error: {ex.Message}";
            }
        }

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        //  PREVIEW EXTRACTION  (reads input file, fills job metadata)
        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private async Task ExtractPreviewAsync(ConversionJob job, string delimiter, bool hasHeader, CancellationToken ct)
        {
            var previewRows = new List<string[]>();
            int lineNumber = 0;

            using var reader = new StreamReader(job.InputFilePath!, Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true, bufferSize: 65536);

            string? line;
            while ((line = await reader.ReadLineAsync(ct)) != null && lineNumber <= _settings.PreviewRowCount)
            {
                var fields = SplitLine(line, delimiter);

                if (lineNumber == 0)
                {
                    // Determine column names
                    job.ColumnCount = fields.Length;
                    if (hasHeader)
                    {
                        job.ColumnNames = fields.Select((f, i) => string.IsNullOrWhiteSpace(f) ? $"Column{i + 1}" : f.Trim()).ToArray();
                    }
                    else
                    {
                        job.ColumnNames = Enumerable.Range(1, fields.Length).Select(i => $"Column{i}").ToArray();
                        previewRows.Add(fields); // first line is data, not header
                    }
                }
                else
                {
                    if (previewRows.Count < _settings.PreviewRowCount)
                        previewRows.Add(fields);
                }

                lineNumber++;
            }

            job.PreviewRows = previewRows.ToArray();
        }

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        //  CSV OUTPUT  (fully streaming â€” no full-file memory load)
        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private async Task ConvertToCsvAsync(ConversionJob job, string delimiter, bool hasHeader, CancellationToken ct)
        {
            job.StatusMessage = "Converting to CSV...";
            long lineNumber = 0;
            long bytesRead = 0;

            using var reader = new StreamReader(job.InputFilePath!, Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true, bufferSize: 65536);
            await using var writer = new StreamWriter(job.OutputFilePath!, false,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), bufferSize: 65536);

            string? line;
            while ((line = await reader.ReadLineAsync(ct)) != null)
            {
                ct.ThrowIfCancellationRequested();

                lineNumber++;
                bytesRead += Encoding.UTF8.GetByteCount(line) + 2;

                var fields = SplitLine(line, delimiter);
                var csvLine = BuildCsvLine(fields, isHeader: hasHeader && lineNumber == 1);
                await writer.WriteAsync(csvLine);
                await writer.WriteAsync("\r\n");

                if (lineNumber % _settings.ProgressReportEveryNLines == 0)
                    UpdateProgress(job, lineNumber, bytesRead);
            }

            job.ProcessedLines = lineNumber;
            job.ProcessedBytes = bytesRead;
        }

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        //  EXCEL OUTPUT  (ClosedXML, row-by-row)
        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private async Task ConvertToExcelAsync(ConversionJob job, string delimiter, bool hasHeader, CancellationToken ct)
        {
            job.StatusMessage = "Converting to Excel...";
            long lineNumber = 0;
            long bytesRead = 0;

            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Data");
            int excelRow = 1;

            using var reader = new StreamReader(job.InputFilePath!, Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true, bufferSize: 65536);

            string? line;
            while ((line = await reader.ReadLineAsync(ct)) != null)
            {
                ct.ThrowIfCancellationRequested();

                lineNumber++;
                bytesRead += Encoding.UTF8.GetByteCount(line) + 2;

                if (lineNumber > _settings.ExcelMaxRows)
                {
                    job.StatusMessage = $"Warning: Excel row limit ({_settings.ExcelMaxRows:N0}) reached. Remaining rows skipped.";
                    break;
                }

                var fields = SplitLine(line, delimiter);
                bool isHeaderRow = hasHeader && excelRow == 1;

                for (int col = 0; col < fields.Length; col++)
                {
                    var cell = worksheet.Cell(excelRow, col + 1);
                    // Force text format on ALL cells â€” prevents Excel from converting
                    // large numbers (1.20183E+15), stripping leading zeros (phone numbers),
                    // or mangling values with special chars.
                    cell.Style.NumberFormat.Format = "@";
                    cell.Value = fields[col];
                }

                if (isHeaderRow)
                {
                    var headerRow = worksheet.Row(1);
                    headerRow.Style.Font.Bold = true;
                    headerRow.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e3a5f");
                    headerRow.Style.Font.FontColor = XLColor.White;
                }

                excelRow++;

                if (lineNumber % _settings.ProgressReportEveryNLines == 0)
                    UpdateProgress(job, lineNumber, bytesRead);
            }

            if (hasHeader && excelRow > 1)
                worksheet.SheetView.FreezeRows(1);

            int colCount = Math.Min(worksheet.LastColumnUsed()?.ColumnNumber() ?? 1, 50);
            for (int c = 1; c <= colCount; c++)
                worksheet.Column(c).AdjustToContents(1, Math.Min(excelRow - 1, 500));

            workbook.SaveAs(job.OutputFilePath);

            job.ProcessedLines = lineNumber;
            job.ProcessedBytes = bytesRead;
        }

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        //  CORE PARSING â€” split on delimiter (no quoting assumed in input)
        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private static string[] SplitLine(string line, string delimiter)
        {
            if (string.IsNullOrEmpty(delimiter))
                return [line];
            return line.Split(delimiter);
        }

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        //  RFC 4180 CSV BUILDER
        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private static string BuildCsvLine(string[] fields, bool isHeader)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < fields.Length; i++)
            {
                if (i > 0) sb.Append(',');
                AppendCsvField(sb, fields[i], isHeader);
            }
            return sb.ToString();
        }

        private static void AppendCsvField(StringBuilder sb, string value, bool isHeader)
        {
            // Header fields: plain RFC 4180 â€” no numeric wrapping needed
            if (isHeader)
            {
                AppendRfc4180(sb, value);
                return;
            }

            // â”€â”€ Excel numeric-safety guard â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            // Wrap with ="value" when the field:
            //   (a) has a leading zero followed by more digits â€” Excel strips the zero
            //       e.g. mobile "01711234567" â†’ Excel shows "1711234567"
            //   (b) is purely numeric with >15 significant digits â€” Excel rounds
            //       and displays as scientific notation e.g. "7.47067E+11"
            // Both cases are handled by IsExcelNumericProblematic().
            if (IsExcelNumericProblematic(value))
            {
                // ="exact_value"  â€” Excel evaluates to text string, preserves digits exactly
                sb.Append("=\"");
                // Escape any embedded double-quotes inside the value
                foreach (char c in value)
                {
                    if (c == '"') sb.Append("\"\"");
                    else sb.Append(c);
                }
                sb.Append('"');
                return;
            }

            // â”€â”€ Standard RFC 4180 â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            AppendRfc4180(sb, value);
        }

        /// <summary>
        /// Standard RFC 4180 field encoding:
        /// wrap in double-quotes if value contains comma, double-quote, CR, or LF;
        /// escape internal double-quotes as "".
        /// </summary>
        private static void AppendRfc4180(StringBuilder sb, string value)
        {
            bool needsQuoting = value.Contains(',')
                             || value.Contains('"')
                             || value.Contains('\r')
                             || value.Contains('\n');

            if (!needsQuoting)
            {
                sb.Append(value);
                return;
            }

            sb.Append('"');
            foreach (char c in value)
            {
                if (c == '"') sb.Append("\"\"");
                else sb.Append(c);
            }
            sb.Append('"');
        }

        /// <summary>
        /// Returns true when a field value is purely numeric (digits, optional single decimal
        /// point, optional leading minus). ALL such values are wrapped with ="value" in CSV so
        /// that Excel treats them as text strings â€” preserving leading zeros, full digit
        /// sequences, and preventing any scientific-notation conversion regardless of length.
        /// </summary>
        private static bool IsExcelNumericProblematic(string value)
        {
            var trimmed = value.AsSpan().Trim();
            if (trimmed.IsEmpty) return false;

            // Optional leading minus
            var rest = trimmed[0] == '-' ? trimmed[1..] : trimmed;
            if (rest.IsEmpty) return false;

            bool seenDot = false;
            int digitCount = 0;

            foreach (char c in rest)
            {
                if (c == '.' && !seenDot) { seenDot = true; continue; }
                if (c < '0' || c > '9') return false; // contains non-numeric â†’ leave as-is
                digitCount++;
            }

            // Must have at least one digit to be considered numeric
            return digitCount > 0;
        }

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        //  PROGRESS HELPER
        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private static void UpdateProgress(ConversionJob job, long lineNumber, long bytesRead)
        {
            job.ProcessedLines = lineNumber;
            job.ProcessedBytes = Math.Min(bytesRead, job.TotalBytes);
            job.ProgressPercent = job.TotalBytes > 0
                ? (int)Math.Min(99, bytesRead * 100 / job.TotalBytes)
                : 0;
            job.StatusMessage = $"Processing... {lineNumber:N0} rows converted";
        }
    }
}
