namespace BulkDataEngine.Models
{
    public class ConverterSettings
    {
        public string DefaultDelimiter { get; set; } = "~";
        public bool DefaultHasHeader { get; set; } = true;
        public int MaxFileSizeMB { get; set; } = 500;
        public int MaxExcelFileSizeMB { get; set; } = 50;
        public string TempDirectory { get; set; } = "temp";
        public int ProgressReportEveryNLines { get; set; } = 5000;
        public int JobExpiryMinutes { get; set; } = 30;
        public string[] AllowedInputExtensions { get; set; } = [".txt", ".csv", ".tsv", ".dat", ".log"];
        public int ExcelMaxRows { get; set; } = 1048576;
        public string DefaultOutputFormat { get; set; } = "csv";
        public int PreviewRowCount { get; set; } = 10;
    }
}
