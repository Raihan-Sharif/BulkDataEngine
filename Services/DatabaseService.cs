using Microsoft.Data.SqlClient;
using System.Data;
using System.Text.RegularExpressions;
using BulkDataEngine.Models;

namespace BulkDataEngine.Services
{
    public class DatabaseService : IDatabaseService
    {
        private readonly string _defaultConnectionString;
        private readonly DatabaseSettings _dbSettings;
        private readonly ILogger<DatabaseService> _logger;

        public DatabaseService(IConfiguration config, DatabaseSettings dbSettings, ILogger<DatabaseService> logger)
        {
            var defaultConnName = config.GetValue<string>("DefaultConnection");
            _defaultConnectionString = !string.IsNullOrEmpty(defaultConnName) 
                ? config.GetConnectionString(defaultConnName) ?? string.Empty
                : string.Empty;
            _dbSettings = dbSettings;
            _logger = logger;
        }

        // â”€â”€ TABLE NAME GENERATOR â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public string GenerateTableName(string originalFileName, DatabaseSettings settings)
        {
            var baseName = Path.GetFileNameWithoutExtension(originalFileName);
            baseName = Regex.Replace(baseName, @"[^A-Za-z0-9_]", "_");
            if (baseName.Length > 0 && char.IsDigit(baseName[0])) baseName = "_" + baseName;
            if (baseName.Length > 80) baseName = baseName[..80];
            var date = settings.IncludeDateInTableName ? $"_{DateTime.Now:yyyyMMdd}" : string.Empty;
            return $"{baseName}_{settings.TableSuffix}{date}";
        }

        // â”€â”€ MAIN INSERT ENTRY POINT â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public async Task InsertAsync(
            ConversionJob job,
            string tableName,
            string[] columnNames,
            IAsyncEnumerable<string[]> dataRows,
            long totalRows,
            string? customConnectionString,
            CancellationToken ct)
        {
            var connStr = !string.IsNullOrWhiteSpace(customConnectionString)
                ? customConnectionString
                : _defaultConnectionString;

            job.DbStatus         = JobStatus.Processing;
            job.DbTableName      = tableName;
            job.DbTotalRows      = totalRows;
            job.DbInsertedRows   = 0;
            job.DbProgressPercent = 0;
            job.DbStatusMessage  = "Connecting to database...";
            job.DbErrorMessage   = null;

            try
            {
                if (columnNames.Length == 0)
                    throw new InvalidOperationException("Column metadata is missing. Process the file first.");

                await using var connection = new SqlConnection(connStr);
                await connection.OpenAsync(ct);

                job.DbStatusMessage = $"Creating table [{tableName}]...";
                await CreateTableAsync(connection, tableName, columnNames, ct);

                job.DbStatusMessage = "Inserting data...";
                await BulkInsertAsync(connection, job, tableName, columnNames, dataRows, totalRows, ct);

                job.DbStatus          = JobStatus.Completed;
                job.DbProgressPercent = 100;
                job.DbStatusMessage   = $"Done â€” {job.DbInsertedRows:N0} rows inserted into [{tableName}].";
            }
            catch (OperationCanceledException)
            {
                job.DbStatus        = JobStatus.Cancelled;
                job.DbStatusMessage = "Database insert cancelled.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DB insert failed for job {JobId}", job.JobId);
                job.DbStatus        = JobStatus.Failed;
                job.DbErrorMessage  = ex.Message;
                job.DbStatusMessage = $"Error: {ex.Message}";
            }
        }

        // â”€â”€ CREATE TABLE â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private async Task CreateTableAsync(SqlConnection connection, string tableName,
            string[] columnNames, CancellationToken ct)
        {
            var colDefs = string.Join(",\r\n    ",
                columnNames.Select(c => $"[{Esc(c)}] NVARCHAR(MAX) NULL"));

            var sql = $"""
                IF OBJECT_ID(N'[dbo].[{Esc(tableName)}]', N'U') IS NOT NULL
                    DROP TABLE [dbo].[{Esc(tableName)}];

                CREATE TABLE [dbo].[{Esc(tableName)}]
                (
                    [_RowId] INT IDENTITY(1,1) NOT NULL,
                    {colDefs},
                    CONSTRAINT [PK_{Esc(tableName)}] PRIMARY KEY CLUSTERED ([_RowId] ASC)
                );
                """;

            await using var cmd = new SqlCommand(sql, connection) { CommandTimeout = _dbSettings.CommandTimeoutSeconds };
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // â”€â”€ BULK INSERT â€” streams data from any IAsyncEnumerable source â”€â”€â”€â”€â”€â”€â”€â”€
        private async Task BulkInsertAsync(
            SqlConnection connection,
            ConversionJob job,
            string tableName,
            string[] columnNames,
            IAsyncEnumerable<string[]> dataRows,
            long totalRows,
            CancellationToken ct)
        {
            long insertedRows = 0;
            var dt = BuildDataTable(columnNames);

            await foreach (var fields in dataRows.WithCancellation(ct))
            {
                ct.ThrowIfCancellationRequested();

                var row = dt.NewRow();
                for (int col = 0; col < columnNames.Length; col++)
                    row[col] = col < fields.Length ? (object)fields[col] : DBNull.Value;
                dt.Rows.Add(row);

                if (dt.Rows.Count >= _dbSettings.BulkBatchSize)
                {
                    await FlushBatchAsync(connection, dt, tableName, ct);
                    insertedRows += dt.Rows.Count;
                    dt.Clear();

                    job.DbInsertedRows    = insertedRows;
                    job.DbProgressPercent = totalRows > 0
                        ? (int)Math.Min(99, insertedRows * 100 / totalRows)
                        : 0;
                    job.DbStatusMessage = $"Inserting... {insertedRows:N0} rows done";
                }
            }

            // Final partial batch
            if (dt.Rows.Count > 0)
            {
                await FlushBatchAsync(connection, dt, tableName, ct);
                insertedRows += dt.Rows.Count;
                dt.Clear();
            }

            job.DbInsertedRows = insertedRows;
        }

        private async Task FlushBatchAsync(SqlConnection connection, DataTable dt,
            string tableName, CancellationToken ct)
        {
            await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(ct);
            try
            {
                using var bulk = new SqlBulkCopy(connection, SqlBulkCopyOptions.Default, tx)
                {
                    DestinationTableName = $"[dbo].[{Esc(tableName)}]",
                    BatchSize            = dt.Rows.Count,
                    BulkCopyTimeout      = _dbSettings.CommandTimeoutSeconds
                };
                foreach (DataColumn col in dt.Columns)
                    bulk.ColumnMappings.Add(col.ColumnName, $"[{Esc(col.ColumnName)}]");

                await bulk.WriteToServerAsync(dt, ct);
                await tx.CommitAsync(ct);
            }
            catch
            {
                await tx.RollbackAsync(CancellationToken.None); // don't cancel the rollback
                throw;
            }
        }

        private static DataTable BuildDataTable(string[] columnNames)
        {
            var dt = new DataTable();
            foreach (var name in columnNames)
                dt.Columns.Add(new DataColumn(Esc(name), typeof(string)));
            return dt;
        }

        private static string Esc(string name) => name.Replace("]", "]]");
    }
}
