namespace BulkDataEngine.Models
{
    public class DatabaseSettings
    {
        public string TableSuffix { get; set; } = "temp";
        public bool IncludeDateInTableName { get; set; } = true;
        public int BulkBatchSize { get; set; } = 5000;
        public int ProgressReportEveryNRows { get; set; } = 1000;
        public int CommandTimeoutSeconds { get; set; } = 300;
    }
}
