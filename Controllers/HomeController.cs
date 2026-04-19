using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using BulkDataEngine.Models;
using BulkDataEngine.Services;

namespace BulkDataEngine.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly ConverterSettings _settings;
        private readonly DatabaseSettings _dbSettings;
        private readonly ConversionJobManager _jobManager;
        private readonly IConversionService _conversionService;
        private readonly IDatabaseService _databaseService;
        private readonly IImportService _importService;
        private readonly IWebHostEnvironment _env;
        private readonly IConfiguration _config;

        private static readonly JsonSerializerOptions _json =
            new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        public HomeController(
            ILogger<HomeController> logger,
            ConverterSettings settings,
            DatabaseSettings dbSettings,
            ConversionJobManager jobManager,
            IConversionService conversionService,
            IDatabaseService databaseService,
            IImportService importService,
            IWebHostEnvironment env,
            IConfiguration config)
        {
            _logger = logger;
            _settings = settings;
            _dbSettings = dbSettings;
            _jobManager = jobManager;
            _conversionService = conversionService;
            _databaseService = databaseService;
            _importService = importService;
            _env = env;
            _config = config;
        }

        // â”€â”€ INDEX â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public IActionResult Index()
        {
            ViewBag.Settings = _settings;
            var connStrings = _config.GetSection("ConnectionStrings").GetChildren()
                .ToDictionary(x => x.Key, x => x.Value);
            ViewBag.Connections = connStrings;
            ViewBag.DefaultConnectionName = _config.GetValue<string>("DefaultConnection") ?? connStrings.Keys.FirstOrDefault();
            return View();
        }

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        //  TAB 1 â€” CONVERT TXT
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

        [HttpPost]
        [DisableRequestSizeLimit]
        [RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue)]
        public async Task<IActionResult> Upload(IFormFile file, string delimiter,
            bool hasHeader, string outputFormat)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { error = "No file provided." });

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!_settings.AllowedInputExtensions.Contains(ext))
                return BadRequest(new { error = $"File type '{ext}' not allowed. Allowed: {string.Join(", ", _settings.AllowedInputExtensions)}" });

            long maxBytes = (long)_settings.MaxFileSizeMB * 1024 * 1024;
            if (file.Length > maxBytes)
                return BadRequest(new { error = $"File exceeds {_settings.MaxFileSizeMB} MB limit." });

            if (outputFormat.Equals("xlsx", StringComparison.OrdinalIgnoreCase))
            {
                long maxExcel = (long)_settings.MaxExcelFileSizeMB * 1024 * 1024;
                if (file.Length > maxExcel)
                    return BadRequest(new { error = $"Excel output limited to {_settings.MaxExcelFileSizeMB} MB. Use CSV for larger files." });
            }

            delimiter = NormalizeDelimiter(delimiter);

            var job = _jobManager.CreateJob(file.FileName, outputFormat);
            job.FileExtension = ext;
            job.Delimiter = delimiter;
            job.HasHeader = hasHeader;

            var tempDir = Path.Combine(_env.ContentRootPath, _settings.TempDirectory);
            job.InputFilePath = Path.Combine(tempDir, $"{job.JobId}_input{ext}");

            var outExt = outputFormat.Equals("xlsx", StringComparison.OrdinalIgnoreCase) ? ".xlsx" : ".csv";
            job.OutputFileName = $"{Path.GetFileNameWithoutExtension(file.FileName)}_converted{outExt}";
            job.OutputFilePath = Path.Combine(tempDir, $"{job.JobId}_output{outExt}");
            job.TotalBytes = file.Length;
            job.Status = JobStatus.Uploading;
            job.StatusMessage = "Uploading file...";

            await using (var fs = System.IO.File.Create(job.InputFilePath))
                await file.CopyToAsync(fs);

            _ = Task.Run(async () =>
            {
                try { await _conversionService.ConvertAsync(job, delimiter, hasHeader, job.CancellationTokenSource.Token); }
                catch (Exception ex) { _logger.LogError(ex, "Conversion failed {JobId}", job.JobId); }
            });

            return Ok(new { jobId = job.JobId });
        }

        [HttpGet]
        public async Task Progress(string jobId, CancellationToken clientDisconnected)
        {
            SetSSEHeaders(Response);
            var lastKeepalive = DateTime.UtcNow;

            while (!clientDisconnected.IsCancellationRequested)
            {
                var job = _jobManager.GetJob(jobId);
                if (job == null)
                {
                    await WriteSSE(Response, new { status = "notfound" });
                    break;
                }

                await WriteSSE(Response, new
                {
                    status           = job.Status.ToString().ToLower(),
                    percent          = job.ProgressPercent,
                    processedLines   = job.ProcessedLines,
                    totalBytes       = job.TotalBytes,
                    processedBytes   = job.ProcessedBytes,
                    message          = job.StatusMessage,
                    error            = job.ErrorMessage,
                    outputFileName   = job.Status == JobStatus.Completed ? job.OutputFileName : null,
                    outputSizeBytes  = job.OutputFileSizeBytes,
                    columnCount      = job.ColumnCount,
                    columnNames      = job.Status == JobStatus.Completed ? job.ColumnNames : null,
                    previewRows      = job.Status == JobStatus.Completed ? job.PreviewRows  : null,
                    suggestedTable   = job.Status == JobStatus.Completed
                        ? _databaseService.GenerateTableName(job.OriginalFileName, _dbSettings) : null,
                    jobId            = job.JobId
                });

                if (job.Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled)
                    break;

                lastKeepalive = await SendKeepaliveIfDue(Response, lastKeepalive);
                await Task.Delay(400, clientDisconnected).ContinueWith(_ => { });
            }
        }

        [HttpGet]
        public IActionResult Download(string jobId)
        {
            var job = _jobManager.GetJob(jobId);
            if (job is null || job.Status != JobStatus.Completed || job.OutputFilePath is null)
                return NotFound("File not found or conversion not complete.");
            if (!System.IO.File.Exists(job.OutputFilePath))
                return NotFound("Output file was removed.");

            var contentType = job.OutputFormat.Equals("xlsx", StringComparison.OrdinalIgnoreCase)
                ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
                : "text/csv";

            var bytes = System.IO.File.ReadAllBytes(job.OutputFilePath);
            _ = Task.Run(async () => { await Task.Delay(30_000); _jobManager.RemoveJob(jobId); });
            return File(bytes, contentType, job.OutputFileName);
        }

        [HttpPost]
        public IActionResult Cancel(string jobId)
        {
            _jobManager.GetJob(jobId)?.CancellationTokenSource.Cancel();
            return Ok();
        }

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        //  TAB 2 â€” DIRECT IMPORT (CSV / Excel)
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

        private static readonly string[] _importExtensions = [".csv", ".xlsx", ".xls", ".tsv"];

        [HttpPost]
        [DisableRequestSizeLimit]
        [RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue)]
        public async Task<IActionResult> ImportUpload(IFormFile file, string delimiter, bool hasHeader)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { error = "No file provided." });

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!_importExtensions.Contains(ext))
                return BadRequest(new { error = $"Allowed: {string.Join(", ", _importExtensions)}" });

            long maxBytes = (long)_settings.MaxFileSizeMB * 1024 * 1024;
            if (file.Length > maxBytes)
                return BadRequest(new { error = $"File exceeds {_settings.MaxFileSizeMB} MB limit." });

            delimiter = NormalizeDelimiter(delimiter);

            var job = _jobManager.CreateJob(file.FileName, "import");
            job.Type = JobType.DirectImport;
            job.FileExtension = ext;
            job.Delimiter = delimiter;
            job.HasHeader = hasHeader;
            job.TotalBytes = file.Length;

            var tempDir = Path.Combine(_env.ContentRootPath, _settings.TempDirectory);
            job.InputFilePath = Path.Combine(tempDir, $"{job.JobId}_import{ext}");
            job.Status = JobStatus.Uploading;
            job.StatusMessage = "Uploading...";

            await using (var fs = System.IO.File.Create(job.InputFilePath))
                await file.CopyToAsync(fs);

            // Extract metadata (column names, preview, row count)
            try
            {
                job.Status = JobStatus.Processing;
                job.StatusMessage = "Reading file metadata...";
                var (cols, rows, preview) = await _importService.GetMetadataAsync(
                    job.InputFilePath, ext, delimiter, hasHeader, CancellationToken.None);
                job.ColumnNames   = cols;
                job.ColumnCount   = cols.Length;
                job.PreviewRows   = preview;
                job.ProcessedLines = rows;
                job.Status = JobStatus.Completed;
                job.StatusMessage = $"Ready â€” {rows:N0} rows, {cols.Length} columns.";
                job.CompletedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                job.Status = JobStatus.Failed;
                job.ErrorMessage = ex.Message;
                return BadRequest(new { error = ex.Message });
            }

            return Ok(new
            {
                jobId          = job.JobId,
                columnCount    = job.ColumnCount,
                columnNames    = job.ColumnNames,
                previewRows    = job.PreviewRows,
                estimatedRows  = job.ProcessedLines,
                suggestedTable = _databaseService.GenerateTableName(job.OriginalFileName, _dbSettings)
            });
        }

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        //  DATABASE INSERT (shared by both tabs)
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

        [HttpPost]
        public IActionResult InsertDatabase([FromForm] string jobId,
            [FromForm] string tableName, [FromForm] string? customConnStr)
        {
            var job = _jobManager.GetJob(jobId);
            if (job is null)               return NotFound(new { error = "Job not found." });
            if (job.Status != JobStatus.Completed)
                return BadRequest(new { error = "File must be processed before inserting." });
            if (string.IsNullOrWhiteSpace(tableName))
                return BadRequest(new { error = "Table name is required." });

            job.DbCancellationTokenSource = new CancellationTokenSource();
            job.DbStatus          = JobStatus.Queued;
            job.DbStatusMessage   = "Queued";
            job.DbErrorMessage    = null;
            job.DbInsertedRows    = 0;
            job.DbProgressPercent = 0;
            job.DbTableName       = tableName.Trim();
            job.DbTotalRows       = job.ProcessedLines;

            var safeConnStr = customConnStr; // captured for the task closure

            _ = Task.Run(async () =>
            {
                try
                {
                    IAsyncEnumerable<string[]> rows = job.Type == JobType.DirectImport
                        ? _importService.ReadRowsAsync(job.InputFilePath!, job.FileExtension,
                            job.Delimiter, job.HasHeader, job.DbCancellationTokenSource.Token)
                        : ReadTxtRowsAsync(job.InputFilePath!, job.Delimiter, job.HasHeader,
                            job.DbCancellationTokenSource.Token);

                    await _databaseService.InsertAsync(
                        job, tableName.Trim(), job.ColumnNames, rows,
                        job.ProcessedLines, safeConnStr, job.DbCancellationTokenSource.Token);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "DB insert failed for job {JobId}", job.JobId);
                }
            });

            return Ok(new { jobId = job.JobId });
        }

        [HttpGet]
        public async Task DbProgress(string jobId, CancellationToken clientDisconnected)
        {
            SetSSEHeaders(Response);
            var lastKeepalive = DateTime.UtcNow;

            while (!clientDisconnected.IsCancellationRequested)
            {
                var job = _jobManager.GetJob(jobId);
                if (job is null)
                {
                    await WriteSSE(Response, new { status = "notfound" });
                    break;
                }

                await WriteSSE(Response, new
                {
                    status       = job.DbStatus.ToString().ToLower(),
                    percent      = job.DbProgressPercent,
                    insertedRows = job.DbInsertedRows,
                    totalRows    = job.DbTotalRows,
                    message      = job.DbStatusMessage,
                    error        = job.DbErrorMessage,
                    tableName    = job.DbTableName
                });

                if (job.DbStatus is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled)
                    break;

                lastKeepalive = await SendKeepaliveIfDue(Response, lastKeepalive);
                await Task.Delay(400, clientDisconnected).ContinueWith(_ => { });
            }
        }

        /// <summary>Lightweight status poll â€” used as SSE fallback when connection drops.</summary>
        [HttpGet]
        public IActionResult DbJobStatus(string jobId)
        {
            var job = _jobManager.GetJob(jobId);
            if (job is null) return NotFound();
            return Ok(new
            {
                status       = job.DbStatus.ToString().ToLower(),
                percent      = job.DbProgressPercent,
                insertedRows = job.DbInsertedRows,
                totalRows    = job.DbTotalRows,
                message      = job.DbStatusMessage,
                error        = job.DbErrorMessage,
                tableName    = job.DbTableName
            });
        }

        [HttpPost]
        public IActionResult CancelDb(string jobId)
        {
            _jobManager.GetJob(jobId)?.DbCancellationTokenSource.Cancel();
            return Ok();
        }

        // â”€â”€ ERROR â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error() =>
            View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        //  HELPERS
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

        /// <summary>Streams rows from a TXT file, splitting by delimiter. Skips header if needed.</summary>
        private static async IAsyncEnumerable<string[]> ReadTxtRowsAsync(
            string filePath, string delimiter, bool hasHeader,
            [EnumeratorCancellation] CancellationToken ct)
        {
            using var reader = new StreamReader(filePath, Encoding.UTF8, true, 65536);
            bool first = true;
            string? line;
            while ((line = await reader.ReadLineAsync(ct)) != null)
            {
                ct.ThrowIfCancellationRequested();
                if (first) { first = false; if (hasHeader) continue; }
                yield return line.Split(delimiter);
            }
        }

        private static void SetSSEHeaders(HttpResponse response)
        {
            response.Headers.Append("Content-Type",      "text/event-stream");
            response.Headers.Append("Cache-Control",     "no-cache");
            response.Headers.Append("X-Accel-Buffering", "no");
        }

        private static async Task WriteSSE(HttpResponse response, object data)
        {
            var json = JsonSerializer.Serialize(data, _json);
            await response.WriteAsync($"data: {json}\n\n");
            await response.Body.FlushAsync();
        }

        /// <summary>Sends an SSE comment keepalive every 15 seconds to prevent proxy/browser timeout.</summary>
        private static async Task<DateTime> SendKeepaliveIfDue(HttpResponse response, DateTime lastKeepalive)
        {
            if ((DateTime.UtcNow - lastKeepalive).TotalSeconds >= 15)
            {
                await response.WriteAsync(": keepalive\n\n");
                await response.Body.FlushAsync();
                return DateTime.UtcNow;
            }
            return lastKeepalive;
        }

        private static string NormalizeDelimiter(string raw) => raw switch
        {
            null or "" => "~",
            "tab" or "\\t" => "\t",
            "pipe"       => "|",
            "semicolon"  => ";",
            "comma"      => ",",
            "tilde"      => "~",
            _            => raw
        };


    }
}
