using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Threading;

namespace Strivea.Services
{
    public class RecordingStatsLogger : IDisposable
    {
        private readonly string _statsFilePath;
        private readonly ILogger<RecordingStatsLogger> _logger;
        private readonly SemaphoreSlim _fileLock = new SemaphoreSlim(1, 1);
        private bool _disposed = false;

        public RecordingStatsLogger(ILogger<RecordingStatsLogger> logger)
        {
            _logger = logger;
            _statsFilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Strivea",
                "Logs",
                "recording_stats.log");
        }

        private void EnsureDirectoryExists()
        {
            var directory = Path.GetDirectoryName(_statsFilePath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        public async Task LogRecordingStatsAsync(string url, string outputPath, string platform)
        {
            try
            {
                await _fileLock.WaitAsync();
                try
                {
                    EnsureDirectoryExists();
                    
                    if (!File.Exists(outputPath))
                    {
                        _logger.LogError($"Fichier d'enregistrement non trouvé : {outputPath}");
                        return;
                    }

                    var fileInfo = new FileInfo(outputPath);
                    var stats = new
                    {
                        Timestamp = DateTime.Now,
                        Url = url,
                        Platform = platform,
                        FilePath = outputPath,
                        FileSize = fileInfo.Length,
                        Duration = TimeSpan.Zero // TODO: Implémenter la détection de la durée
                    };

                    var logEntry = $"{stats.Timestamp:yyyy-MM-dd HH:mm:ss} | {stats.Platform} | {stats.Url} | {stats.FilePath} | {stats.FileSize} bytes | {stats.Duration}\n";
                    await File.AppendAllTextAsync(_statsFilePath, logEntry);
                    _logger.LogInformation($"Statistiques d'enregistrement enregistrées : {outputPath}");
                }
                finally
                {
                    _fileLock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'enregistrement des statistiques");
            }
        }

        public async Task LogAsync(DateTime startTime, long bytesRecorded)
        {
            try
            {
                await _fileLock.WaitAsync();
                try
                {
                    EnsureDirectoryExists();
                    
                    var duration = DateTime.Now - startTime;
                    var stats = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Durée: {duration:hh\\:mm\\:ss}, Taille: {bytesRecorded / 1024.0 / 1024.0:F2} MB{Environment.NewLine}";
                    await File.AppendAllTextAsync(_statsFilePath, stats);
                }
                finally
                {
                    _fileLock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'enregistrement des statistiques");
            }
        }

        // Méthodes synchrones pour la compatibilité (dépréciées)
        [Obsolete("Utilisez LogRecordingStatsAsync à la place")]
        public void LogRecordingStats(string url, string outputPath, string platform)
        {
            _ = LogRecordingStatsAsync(url, outputPath, platform);
        }

        [Obsolete("Utilisez LogAsync à la place")]
        public void Log(DateTime startTime, long bytesRecorded)
        {
            _ = LogAsync(startTime, bytesRecorded);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                try
                {
                    _fileLock?.Dispose();
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Erreur lors du dispose de RecordingStatsLogger");
                }
                finally
                {
                    _disposed = true;
                }
            }
        }

        ~RecordingStatsLogger()
        {
            Dispose(false);
        }
    }
}