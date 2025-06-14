using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Strivea.Services
{
    public class RecordingStatsLogger
    {
        private readonly string _statsFilePath;
        private readonly ILogger<RecordingStatsLogger> _logger;

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

        public void LogRecordingStats(string url, string outputPath, string platform)
        {
            try
            {
                var fileInfo = new FileInfo(outputPath);
                if (!fileInfo.Exists)
                {
                    _logger.LogError($"Fichier non trouvé : {outputPath}");
                    return;
                }

                var stats = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}|{url}|{platform}|{fileInfo.Length}|{fileInfo.CreationTime}|{fileInfo.LastWriteTime}";
                File.AppendAllText(_statsFilePath, stats + Environment.NewLine);
                _logger.LogInformation($"Statistiques enregistrées : {stats}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'enregistrement des statistiques");
            }
        }

        public async Task LogRecordingStatsAsync(string url, string outputPath, string platform)
        {
            try
            {
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'enregistrement des statistiques");
            }
        }

        public void Log(DateTime startTime, long bytesRecorded)
        {
            try
            {
                var duration = DateTime.Now - startTime;
                var stats = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Durée: {duration:hh\\:mm\\:ss}, Taille: {bytesRecorded / 1024.0 / 1024.0:F2} MB{Environment.NewLine}";
                File.AppendAllText(_statsFilePath, stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'enregistrement des statistiques");
            }
        }
    }
}