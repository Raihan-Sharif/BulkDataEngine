namespace BulkDataEngine.Models
{
    public class ConversionRequest
    {
        public string JobId { get; set; } = string.Empty;
        public string Delimiter { get; set; } = "~";
        public bool HasHeader { get; set; } = true;
        public string OutputFormat { get; set; } = "csv";
        public string OriginalFileName { get; set; } = string.Empty;
    }
}
