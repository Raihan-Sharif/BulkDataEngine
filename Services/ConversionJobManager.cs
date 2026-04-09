using System.Collections.Concurrent;
using BulkDataEngine.Models;

namespace BulkDataEngine.Services
{
    public class ConversionJobManager : IHostedService, IDisposable
    {
        private readonly ConcurrentDictionary<string, ConversionJob> _jobs = new();
        private readonly ConverterSettings _settings;
        private Timer? _cleanupTimer;

        public ConversionJobManager(ConverterSettings settings)
        {
            _settings = settings;
        }

        public ConversionJob CreateJob(string originalFileName, string outputFormat)
        {
            var job = new ConversionJob
            {
                OriginalFileName = originalFileName,
                OutputFormat = outputFormat,
                Status = JobStatus.Queued
            };
            _jobs[job.JobId] = job;
            return job;
        }

        public ConversionJob? GetJob(string jobId) =>
            _jobs.TryGetValue(jobId, out var job) ? job : null;

        public void RemoveJob(string jobId)
        {
            if (_jobs.TryRemove(jobId, out var job))
            {
                // Clean up temp files
                TryDeleteFile(job.InputFilePath);
                TryDeleteFile(job.OutputFilePath);
                job.CancellationTokenSource.Dispose();
            }
        }

        private static void TryDeleteFile(string? path)
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                try { File.Delete(path); } catch { /* ignore */ }
            }
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _cleanupTimer = new Timer(CleanupExpiredJobs, null,
                TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _cleanupTimer?.Change(Timeout.Infinite, 0);
            return Task.CompletedTask;
        }

        private void CleanupExpiredJobs(object? state)
        {
            var expiry = TimeSpan.FromMinutes(_settings.JobExpiryMinutes);
            var expired = _jobs.Values
                .Where(j => DateTime.UtcNow - j.CreatedAt > expiry)
                .ToList();

            foreach (var job in expired)
                RemoveJob(job.JobId);
        }

        public void Dispose()
        {
            _cleanupTimer?.Dispose();
        }
    }
}
