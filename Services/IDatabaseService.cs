using BulkDataEngine.Models;

namespace BulkDataEngine.Services
{
    public interface IDatabaseService
    {
        /// <summary>
        /// Bulk-inserts rows from any async data source into an auto-created SQL Server table.
        /// Uses batched transactions. Passes custom connection string if provided,
        /// otherwise falls back to the appsettings value.
        /// </summary>
        Task InsertAsync(
            ConversionJob job,
            string tableName,
            string[] columnNames,
            IAsyncEnumerable<string[]> dataRows,
            long totalRows,
            string? customConnectionString,
            CancellationToken ct);

        string GenerateTableName(string originalFileName, DatabaseSettings settings);
    }
}
