namespace BulkDataEngine.Models
{
    public enum JobStatus  { Queued, Uploading, Processing, Completed, Failed, Cancelled }
    public enum JobType    { TxtConvert, DirectImport }

    public class ConversionJob
    {
        // â”€â”€ Core identity â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public string JobId { get; set; } = Guid.NewGuid().ToString("N");
        public JobType  Type { get; set; } = JobType.TxtConvert;
        public string OriginalFileName { get; set; } = string.Empty;
        public string OutputFileName { get; set; } = string.Empty;
        public string? InputFilePath  { get; set; }
        public string? OutputFilePath { get; set; }
        public string FileExtension   { get; set; } = ".txt";
        public string OutputFormat    { get; set; } = "csv";
        public string Delimiter       { get; set; } = "~";
        public bool   HasHeader       { get; set; } = false;
        public DateTime  CreatedAt    { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt  { get; set; }
        public CancellationTokenSource CancellationTokenSource { get; set; } = new();

        // â”€â”€ Conversion progress â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public JobStatus Status        { get; set; } = JobStatus.Queued;
        public string    StatusMessage { get; set; } = "Queued";
        public string?   ErrorMessage  { get; set; }
        public long TotalBytes     { get; set; }
        public long ProcessedBytes { get; set; }
        public long ProcessedLines { get; set; }
        public int  ProgressPercent { get; set; }
        public long OutputFileSizeBytes { get; set; }

        // â”€â”€ Data metadata (populated after processing) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public int       ColumnCount  { get; set; }
        public string[]  ColumnNames  { get; set; } = [];
        public string[][] PreviewRows { get; set; } = [];

        // â”€â”€ Database insert state â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public JobStatus DbStatus        { get; set; } = JobStatus.Queued;
        public string    DbStatusMessage { get; set; } = string.Empty;
        public string?   DbErrorMessage  { get; set; }
        public long DbInsertedRows   { get; set; }
        public long DbTotalRows      { get; set; }
        public int  DbProgressPercent { get; set; }
        public string? DbTableName   { get; set; }
        public CancellationTokenSource DbCancellationTokenSource { get; set; } = new();
    }
}
