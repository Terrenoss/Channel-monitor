using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AutoStreamRec.Models;

namespace AutoStreamRec.Services
{
    public class StreamRecorderService
    {
        private readonly ILogger<StreamRecorderService> _logger;
        private readonly ILogAction _log;
        private readonly IStatsAction _stats;
        private readonly ExecutableLocator _locator;
        private readonly FileHelper _fileHelper;
        private readonly VideoConverter _converter;
        private readonly List<IStreamDetector> _detectors;
        private Process _currentProcess;
        private DateTime _startTime;
        private long _bytesRecorded;
        private System.Timers.Timer _statsTimer;
        private string _lastRecordedFile;
        private string _detectedQuality;
        private CancellationTokenSource _cancellationTokenSource;
        private bool _isMonitoring;
        private long _lastBytesRecorded;
        private DateTime _lastStatsUpdate;

        public string LastDetectedQuality => _detectedQuality ?? "best";

        public StreamRecorderService(
            ILogger<StreamRecorderService> logger,
            ILogAction logAction,
            IStatsAction statsAction)
        {
            _logger = logger;
            _log = logAction;
            _stats = statsAction;
            _locator = new ExecutableLocator(msg => _log.Log(msg));
            _fileHelper = new FileHelper(msg => _log.Log(msg));
            _converter = new VideoConverter(msg => _log.Log(msg), _locator);

            // Initialiser les détecteurs
            _detectors = new List<IStreamDetector>
            {
                new YouTubeStreamDetector(msg => _log.Log(msg), _locator),
                new TwitchStreamDetector(msg => _log.Log(msg), _locator),
                new KickStreamDetector(msg => _log.Log(msg), _locator),
                new TrovoStreamDetector(msg => _log.Log(msg), _locator),
                new DLiveStreamDetector(msg => _log.Log(msg), _locator),
                new FacebookGamingStreamDetector(msg => _log.Log(msg), _locator),
                new NimoTVStreamDetector(msg => _log.Log(msg), _locator),
                new AfreecaTVStreamDetector(msg => _log.Log(msg), _locator),
                new BilibiliStreamDetector(msg => _log.Log(msg), _locator),
                new VKPlayLiveStreamDetector(msg => _log.Log(msg), _locator),
                new NiconicoStreamDetector(msg => _log.Log(msg), _locator)
            };
        }

        public async Task<bool> CheckDependencies()
        {
            return !string.IsNullOrEmpty(_locator.FindExecutablePath("streamlink"))
                && !string.IsNullOrEmpty(_locator.FindExecutablePath("ffmpeg"));
        }

        public async Task<StreamInfo> GetLiveStreamInfo(string url)
        {
            foreach (var detector in _detectors)
            {
                if (detector.CanHandle(url))
                {
                    return await detector.DetectStream(url);
                }
            }

            _log.Log($"Aucun détecteur ne peut gérer l'URL: {url}");
            return new StreamInfo { IsLive = false };
        }

        public async Task<bool> RecordStream(string url, CancellationToken token)
        {
            var streamInfo = await GetLiveStreamInfo(url);
            if (!streamInfo.IsLive)
            {
                _log.Log("Aucun stream en direct détecté");
                return false;
            }

            _log.Log($"Stream détecté: {streamInfo.StreamerName} - {streamInfo.Title}");
            _log.Log($"Qualité demandée: {streamInfo.Quality}");

            string safeName = _fileHelper.SanitizeFileName(streamInfo.ChannelName);
            string outputDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                "recordings", streamInfo.Platform, safeName);

            if (!await _fileHelper.EnsureDirectoryWritableAsync(outputDir))
                return false;

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string outputFile = Path.Combine(outputDir, $"{timestamp}.ts");
            _lastRecordedFile = outputFile;

            string args = $"\"{url}\" {streamInfo.Quality} --force -o \"{outputFile}\" --retry-streams 30 --retry-max 10 --loglevel debug";
            _log.Log($"Commande Streamlink: {args}");

            _startTime = DateTime.Now;
            _bytesRecorded = 0;
            _lastBytesRecorded = 0;
            _lastStatsUpdate = DateTime.Now;

            var startInfo = new ProcessStartInfo
            {
                FileName = _locator.FindExecutablePath("streamlink"),
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            _currentProcess = Process.Start(startInfo);
            if (_currentProcess == null)
            {
                _log.Log("ERREUR: Impossible de démarrer Streamlink");
                return false;
            }

            _statsTimer = new System.Timers.Timer(1000);
            _statsTimer.Elapsed += (s, e) =>
            {
                try
                {
                    if (File.Exists(outputFile))
                    {
                        long size = new FileInfo(outputFile).Length;
                        if (size > _bytesRecorded)
                        {
                            var now = DateTime.Now;
                            var timeDiff = (now - _lastStatsUpdate).TotalSeconds;
                            var bytesDiff = size - _lastBytesRecorded;
                            string speedStr = "N/A";
                            if (timeDiff > 0 && bytesDiff > 0)
                            {
                                var speed = bytesDiff / timeDiff; // bytes per second
                                speedStr = $"{speed / 1024.0 / 1024.0:F2} MB/s";
                            }
                            _bytesRecorded = size;
                            _lastBytesRecorded = size;
                            _lastStatsUpdate = now;

                            var duration = now - _startTime;
                            var stats = $"Durée: {duration:hh\\:mm\\:ss} | " +
                                      $"Taille: {size / 1024.0 / 1024.0:F2} MB | " +
                                      $"Vitesse: {speedStr}";
                            _stats.Log(stats);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erreur lors de la mise à jour des statistiques");
                }
            };
            _statsTimer.Start();

            _currentProcess.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    if (e.Data.Contains("Found matching stream:"))
                    {
                        _detectedQuality = e.Data.Split(':')[1].Trim();
                        _log.Log($"Qualité réelle détectée : {_detectedQuality}");
                    }
                    else if (e.Data.Contains("[cli][info] Opening stream:"))
                    {
                        // Extraire la qualité réelle
                        var parts = e.Data.Split(":");
                        if (parts.Length > 1)
                        {
                            var qualityPart = parts[1].Trim();
                            var quality = qualityPart.Split(' ')[0]; // ex: '1080p'
                            _detectedQuality = quality;
                            _log.Log($"Qualité réelle détectée : {_detectedQuality}");
                        }
                    }
                    else if (e.Data.Contains("Stream ended"))
                    {
                        _log.Log("Le stream est terminé");
                    }
                    else if (e.Data.Contains("error:"))
                    {
                        _log.Log($"[ERREUR] {e.Data}");
                    }
                    else if (!e.Data.Contains("[stream.hls][debug]"))
                    {
                        _log.Log($"[Streamlink] {e.Data}");
                    }
                }
            };
            _currentProcess.BeginOutputReadLine();
            _currentProcess.BeginErrorReadLine();

            try
            {
                await _currentProcess.WaitForExitAsync(token);
            }
            catch (OperationCanceledException)
            {
                _log.Log("Enregistrement annulé par l'utilisateur");
                throw;
            }
            finally
            {
                _statsTimer?.Stop();
                _statsTimer?.Dispose();
            }

            if (!File.Exists(outputFile) || new FileInfo(outputFile).Length == 0)
            {
                _log.Log("ERREUR: Fichier de sortie invalide");
                return false;
            }

            _log.Log("Début de la conversion en MP4...");
            return await _converter.ConvertToMp4(outputFile);
        }

        public void StopRecording()
        {
            try
            {
                if (_currentProcess != null && !_currentProcess.HasExited)
                {
                    _log.Log("Arrêt du processus Streamlink...");
                    _currentProcess.Kill();
                    _log.Log("Processus arrêté");
                }
            }
            catch (Exception ex)
            {
                _log.Log($"Erreur lors de l'arrêt: {ex.Message}");
            }

            try
            {
                if (!string.IsNullOrEmpty(_lastRecordedFile) && File.Exists(_lastRecordedFile))
                {
                    _log.Log("Conversion du fichier .ts arrêté manuellement...");
                    _converter.ConvertToMp4(_lastRecordedFile);
                }
            }
            catch (Exception ex)
            {
                _log.Log($"Erreur lors de la conversion post-arrêt: {ex.Message}");
            }
        }

        public async Task<bool> ConvertToMp4(string tsPath)
        {
            return await _converter.ConvertToMp4(tsPath);
        }

        public async Task StartMonitoringAsync(string url, CancellationToken cancellationToken)
        {
            if (_isMonitoring)
            {
                throw new InvalidOperationException("La surveillance est déjà en cours");
            }

            _isMonitoring = true;
            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            try
            {
                while (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    try
                    {
                        var streamInfo = await GetLiveStreamInfo(url);
                        if (streamInfo.IsLive)
                        {
                            _logger.LogInformation($"Stream détecté: {streamInfo.StreamerName} - {streamInfo.Title}");
                            _log.Log($"Stream détecté: {streamInfo.StreamerName} - {streamInfo.Title}");
                            
                            // Démarrer l'enregistrement
                            await RecordStream(url, _cancellationTokenSource.Token);
                            
                            // Si l'enregistrement est terminé (stream terminé), on continue la surveillance
                            _log.Log("Retour en mode surveillance...");
                        }
                        else
                        {
                            _log.Log("Aucun stream en direct détecté, nouvelle tentative dans 1 minute...");
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Erreur lors de la surveillance");
                        _log.Log($"Erreur lors de la surveillance: {ex.Message}");
                    }

                    // Attendre 1 minute avant la prochaine vérification
                    await Task.Delay(TimeSpan.FromMinutes(1), _cancellationTokenSource.Token);
                }
            }
            finally
            {
                _isMonitoring = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        public void StopMonitoring()
        {
            _cancellationTokenSource?.Cancel();
            StopRecording();
            _isMonitoring = false;
        }
    }
}

