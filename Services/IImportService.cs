namespace BulkDataEngine.Services
{
    public interface IImportService
    {
        /// <summary>
        /// Reads the first N rows of a CSV or Excel file and returns column names,
        /// estimated total data-row count, and preview rows.
        /// </summary>
        Task<(string[] ColumnNames, long EstimatedRows, string[][] PreviewRows)>
            GetMetadataAsync(string filePath, string fileExt, string csvDelimiter,
                             bool hasHeader, CancellationToken ct);

        /// <summary>
        /// Streams every data row from a CSV or Excel file as a string array.
        /// Header row (if hasHeader) is skipped automatically.
        /// Also strips the ="value" wrapper that our CSV exporter adds for numeric safety.
        /// </summary>
        IAsyncEnumerable<string[]> ReadRowsAsync(string filePath, string fileExt,
            string csvDelimiter, bool hasHeader, CancellationToken ct);
    }
}
