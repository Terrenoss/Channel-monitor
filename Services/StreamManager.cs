using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Strivea.Models;

namespace Strivea.Services
{
    public class StreamManager
    {
        private readonly ILogger<StreamManager> _logger;
        private readonly StreamDetectorFactory _detectorFactory;
        private readonly StreamRecorderService _recorderService;
        private readonly VideoConverter _videoConverter;
        private readonly RecordingStatsLogger _statsLogger;
        private readonly List<BaseStreamDetector> _detectors;
        private string _currentStreamUrl;
        private bool _isMonitoring;

        public StreamManager(
            ILogger<StreamManager> logger,
            StreamDetectorFactory detectorFactory,
            StreamRecorderService recorderService,
            VideoConverter videoConverter,
            RecordingStatsLogger statsLogger,
            IEnumerable<BaseStreamDetector> detectors)
        {
            _logger = logger;
            _detectorFactory = detectorFactory;
            _recorderService = recorderService;
            _videoConverter = videoConverter;
            _statsLogger = statsLogger;
            _detectors = new List<BaseStreamDetector>(detectors);
        }

        public async Task<StreamInfo> GetStreamInfo(string url)
        {
            try
            {
                var detector = _detectorFactory.GetDetector(url);
                if (detector == null)
                {
                    return new StreamInfo
                    {
                        StreamUrl = url,
                        Platform = "Unknown",
                        IsLive = false,
                        ErrorMessage = "Plateforme non supportée"
                    };
                }
                return await detector.GetStreamInfoAsync(url);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erreur lors de la récupération des informations du stream : {url}");
                return new StreamInfo
                {
                    StreamUrl = url,
                    Platform = "Unknown",
                    IsLive = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<bool> StartRecording(string url, string outputPath)
        {
            try
            {
                var streamInfo = await _recorderService.GetStreamInfo(url);
                if (!streamInfo.IsLive)
                {
                    _logger.LogWarning($"Le stream n'est pas en direct : {streamInfo.ErrorMessage}");
                    return false;
                }

                return await _recorderService.RecordStream(url, outputPath, streamInfo.Platform);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors du démarrage de l'enregistrement");
                return false;
            }
        }

        public async Task StopRecording()
        {
            try
            {
                await _recorderService.StopRecording();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'arrêt de l'enregistrement");
            }
        }

        public async Task<bool> StartMonitoringAsync(string url)
        {
            try
            {
                _logger.LogInformation($"Démarrage de la surveillance pour : {url}");

                var detector = _detectorFactory.GetDetector(url);
                if (detector == null)
                {
                    _logger.LogError($"Aucun détecteur trouvé pour l'URL : {url}");
                    return false;
                }

                var streamInfo = await detector.GetStreamInfoAsync(url);
                if (!streamInfo.IsLive)
                {
                    _logger.LogWarning($"Le stream n'est pas en direct : {url}");
                    return false;
                }

                await _recorderService.StartMonitoringAsync(url, new System.Threading.CancellationToken());
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erreur lors du démarrage de la surveillance pour {url}");
                return false;
            }
        }

        public async Task StopMonitoringAsync()
        {
            try
            {
                _logger.LogInformation("Arrêt de la surveillance");
                _recorderService.StopMonitoring();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'arrêt de la surveillance");
            }
        }

        public async Task ConvertRecordingsAsync()
        {
            try
            {
                var recordingsDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "Strivea",
                    "Recordings");

                if (!Directory.Exists(recordingsDirectory))
                {
                    _logger.LogWarning("Le répertoire des enregistrements n'existe pas");
                    return;
                }

                var tsFiles = Directory.GetFiles(recordingsDirectory, "*.ts");
                foreach (var tsFile in tsFiles)
                {
                    var mp4File = Path.ChangeExtension(tsFile, ".mp4");
                    if (!File.Exists(mp4File))
                    {
                        await _videoConverter.ConvertVideoAsync(tsFile, mp4File);
                    }
                }

                _logger.LogInformation("Conversion des enregistrements terminée");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la conversion des enregistrements");
                throw;
            }
        }

        public async Task CleanupAsync()
        {
            try
            {
                var recordingsDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "Strivea",
                    "Recordings");

                if (!Directory.Exists(recordingsDirectory))
                {
                    _logger.LogWarning("Le répertoire des enregistrements n'existe pas");
                    return;
                }

                var tsFiles = Directory.GetFiles(recordingsDirectory, "*.ts");
                foreach (var tsFile in tsFiles)
                {
                    var mp4File = Path.ChangeExtension(tsFile, ".mp4");
                    if (File.Exists(mp4File))
                    {
                        File.Delete(tsFile);
                        _logger.LogInformation($"Fichier supprimé : {tsFile}");
                    }
                }

                _logger.LogInformation("Nettoyage terminé");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors du nettoyage");
                throw;
            }
        }

        private BaseStreamDetector GetDetectorForUrl(string url)
        {
            foreach (var detector in _detectors)
            {
                if (detector.CanHandle(url))
                {
                    return detector;
                }
            }
            return null;
        }
    }
} 