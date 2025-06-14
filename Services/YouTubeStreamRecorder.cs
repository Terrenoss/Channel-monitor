using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Microsoft.Extensions.Logging;
using Strivea.Helpers;

namespace Strivea.Services
{
    public class YouTubeStreamRecorder
    {
        private readonly ILogger<YouTubeStreamRecorder> _logger;
        private readonly FileHelper _fileHelper;
        private readonly YouTubeHelper _ytHelper;
        private readonly RecordingStatsLogger _statsLogger;
        private readonly VideoConverter _videoConverter;
        private readonly ExecutableLocator _executableLocator;

        private Process _recordingProcess;
        private DateTime _startTime;
        private long _bytesRecorded;
        private System.Timers.Timer _statsTimer;
        private string _lastRecordedFile;
        private string _detectedQuality;

        public string LastDetectedQuality => _detectedQuality ?? "best";

        public YouTubeStreamRecorder(
            ILogger<YouTubeStreamRecorder> logger,
            FileHelper fileHelper,
            YouTubeHelper ytHelper,
            RecordingStatsLogger statsLogger,
            VideoConverter videoConverter,
            ExecutableLocator executableLocator)
        {
            _logger = logger;
            _fileHelper = fileHelper;
            _ytHelper = ytHelper;
            _statsLogger = statsLogger;
            _videoConverter = videoConverter;
            _executableLocator = executableLocator;
        }

        public async Task<bool> StartRecording(string url, string outputPath)
        {
            try
            {
                var streamlinkPath = _executableLocator.FindExecutable("streamlink");
                if (string.IsNullOrEmpty(streamlinkPath))
                {
                    _logger.LogError("Streamlink non trouvé");
                    return false;
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = streamlinkPath,
                    Arguments = $"{url} best -o {outputPath}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                _recordingProcess = new Process { StartInfo = startInfo };
                var started = _recordingProcess.Start();
                if (!started)
                {
                    _logger.LogError("ERREUR: Impossible de démarrer Streamlink");
                    return false;
                }

                _logger.LogInformation($"Enregistrement démarré : {url}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors du démarrage de l'enregistrement");
                return false;
            }
        }

        public async Task<bool> StopRecording()
        {
            try
            {
                if (_recordingProcess != null && !_recordingProcess.HasExited)
                {
                    _recordingProcess.Kill();
                    await _recordingProcess.WaitForExitAsync();
                    _logger.LogInformation("Enregistrement arrêté");
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'arrêt de l'enregistrement");
                return false;
            }
        }

        public async Task<bool> RecordStream(string youtubeUrl, CancellationToken token)
        {
            try
            {
                var streamlinkPath = _executableLocator.FindExecutable("streamlink");
                if (string.IsNullOrEmpty(streamlinkPath))
                {
                    _logger.LogError("ERREUR: Streamlink non trouvé");
                    return false;
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = streamlinkPath,
                    Arguments = $"{youtubeUrl} best --force",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                _recordingProcess = Process.Start(startInfo);
                if (_recordingProcess == null)
                {
                    _logger.LogError("ERREUR: Impossible de démarrer Streamlink");
                    return false;
                }

                _startTime = DateTime.Now;
                _bytesRecorded = 0;
                _statsTimer = new System.Timers.Timer(5000);
                _statsTimer.Elapsed += (s, e) => LogStats();
                _statsTimer.Start();

                _recordingProcess.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        _logger.LogInformation($"Streamlink: {e.Data}");
                    }
                };

                _recordingProcess.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        _logger.LogError($"Streamlink Error: {e.Data}");
                    }
                };

                _recordingProcess.BeginOutputReadLine();
                _recordingProcess.BeginErrorReadLine();

                try
                {
                    await _recordingProcess.WaitForExitAsync(token);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Enregistrement annulé");
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'enregistrement");
                return false;
            }
            finally
            {
                try
                {
                    if (_recordingProcess != null && !_recordingProcess.HasExited)
                    {
                        _logger.LogInformation("Arrêt du processus Streamlink...");
                        _recordingProcess.Kill();
                        _logger.LogInformation("Processus arrêté");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erreur lors de l'arrêt du processus");
                }
                finally
                {
                    _statsTimer?.Stop();
                    _statsTimer?.Dispose();
                }
            }
        }

        private void LogStats()
        {
            var duration = DateTime.Now - _startTime;
            _logger.LogInformation($"Durée d'enregistrement : {duration:hh\\:mm\\:ss}");
        }

        public async void Stop()
        {
            try
            {
                if (_recordingProcess != null && !_recordingProcess.HasExited)
                {
                    _logger.LogInformation("Arrêt du processus Streamlink...");
                    _recordingProcess.Kill();
                    _logger.LogInformation("Processus arrêté");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'arrêt");
            }

            try
            {
                if (!string.IsNullOrEmpty(_lastRecordedFile) && File.Exists(_lastRecordedFile))
                {
                    _logger.LogInformation("Conversion du fichier .ts arrêté manuellement...");
                    await _videoConverter.ConvertToMp4(_lastRecordedFile);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la conversion post-arrêt");
            }
        }
    }
}
