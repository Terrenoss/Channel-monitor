using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Strivea.Models;

namespace Strivea.Services
{
    public class StreamRecorderService
    {
        private readonly ILogger<StreamRecorderService> _logger;
        private readonly IExecutableLocator _executableLocator;
        private readonly RecordingStatsLogger _statsLogger;
        private readonly VideoConverter _videoConverter;
        private readonly Action<string> _log;
        private readonly Dictionary<string, IStreamDetector> _detectors;
        private Process _currentProcess;
        private CancellationTokenSource _monitoringCts;
        private bool _isRecording;
        private DateTime _recordingStartTime;
        private long _lastFileSize;
        private string _currentStreamUrl;
        private StreamInfo _currentStreamInfo;
        private readonly TwitchApiService _twitchApiService;

        public StreamRecorderService(
            IExecutableLocator executableLocator,
            ILogger<StreamRecorderService> logger,
            ILoggerFactory loggerFactory,
            RecordingStatsLogger statsLogger,
            VideoConverter videoConverter,
            Action<string> log,
            TwitchApiService twitchApiService)
        {
            _executableLocator = executableLocator;
            _logger = logger;
            _statsLogger = statsLogger;
            _videoConverter = videoConverter;
            _log = log;
            _twitchApiService = twitchApiService;

            _detectors = new Dictionary<string, IStreamDetector>
            {
                { "twitch", new TwitchStreamDetector(_logger, _executableLocator, _twitchApiService) },
                { "kick", new KickStreamDetector(_logger, _executableLocator) },
                { "trovo", new TrovoStreamDetector(_logger, _executableLocator) },
                { "dlive", new DLiveStreamDetector(_logger, _executableLocator) },
                { "facebook", new FacebookGamingStreamDetector(_logger, _executableLocator) },
                { "nimo", new NimoTVStreamDetector(_logger, _executableLocator) },
                { "afreeca", new AfreecaTVStreamDetector(_logger, _executableLocator) },
                { "bilibili", new BilibiliStreamDetector(_logger, _executableLocator) },
                { "vkplay", new VKPlayLiveStreamDetector(_logger, _executableLocator) },
                { "niconico", new NiconicoStreamDetector(_logger, _executableLocator) }
            };
        }

        public async Task<bool> CheckDependencies()
        {
            try
            {
                string streamlinkPath = _executableLocator.FindExecutable("streamlink");
                string ffmpegPath = _executableLocator.FindExecutable("ffmpeg");

                if (string.IsNullOrEmpty(streamlinkPath))
                {
                    _logger.LogError("Streamlink non trouvé");
                    return false;
                }

                if (string.IsNullOrEmpty(ffmpegPath))
                {
                    _logger.LogError("FFmpeg non trouvé");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la vérification des dépendances");
                return false;
            }
        }

        public async Task<StreamInfo> GetStreamInfo(string url)
        {
            foreach (var detector in _detectors.Values)
            {
                if (detector.CanHandle(url))
                {
                    return await detector.GetStreamInfoAsync(url);
                }
            }

            return new StreamInfo
            {
                StreamUrl = url,
                Platform = "Unknown",
                IsLive = false,
                ErrorMessage = "Plateforme non supportée",
                DetectionTime = DateTime.Now
            };
        }

        public async Task StartMonitoringAsync(string url, CancellationToken cancellationToken)
        {
            _currentStreamUrl = url;
            _monitoringCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            while (!_monitoringCts.Token.IsCancellationRequested)
            {
                try
                {
                    _currentStreamInfo = await GetStreamInfo(url);

                    if (_currentStreamInfo.IsLive && !_isRecording)
                    {
                        await StartRecording(url);
                    }
                    else if (!_currentStreamInfo.IsLive && _isRecording)
                    {
                        StopRecording();
                    }

                    await Task.Delay(TimeSpan.FromSeconds(30), _monitoringCts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erreur lors de la surveillance");
                    await Task.Delay(TimeSpan.FromSeconds(30), _monitoringCts.Token);
                }
            }
        }

        public void StopMonitoring()
        {
            _monitoringCts?.Cancel();
            StopRecording();
        }

        private async Task StartRecording(string url)
        {
            if (_isRecording) return;

            try
            {
                string streamlinkPath = _executableLocator.FindExecutable("streamlink");
                string recordingsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "recordings");
                Directory.CreateDirectory(recordingsDir);

                string outputFile = Path.Combine(recordingsDir, "temp.ts");
                string arguments = $"--stream-segment-threads 2 --hls-live-edge 1 --hls-segment-timeout 30 --hls-timeout 60 --retry-streams 5 --retry-open 5 --retry-max 5 --player-external-http --player-external-http-port 0 \"{url}\" best -o \"{outputFile}\"";

                _currentProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = streamlinkPath,
                        Arguments = arguments,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    },
                    EnableRaisingEvents = true
                };

                _currentProcess.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        _logger.LogInformation(e.Data);
                };

                _currentProcess.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        _logger.LogError(e.Data);
                };

                _currentProcess.Exited += (sender, e) =>
                {
                    if (_isRecording)
                    {
                        _logger.LogWarning("Processus d'enregistrement terminé inopinément");
                        StopRecording();
                    }
                };

                _currentProcess.Start();
                _currentProcess.BeginOutputReadLine();
                _currentProcess.BeginErrorReadLine();

                _isRecording = true;
                _recordingStartTime = DateTime.Now;
                _lastFileSize = 0;

                _logger.LogInformation($"Démarrage de l'enregistrement pour {url}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors du démarrage de l'enregistrement");
                StopRecording();
            }
        }

        public async Task<bool> StopRecording()
        {
            try
            {
                if (_currentProcess == null || _currentProcess.HasExited)
                {
                    _logger.LogWarning("Aucun enregistrement en cours");
                    return false;
                }

                _logger.LogInformation("Arrêt de l'enregistrement en cours...");
                _currentProcess.StandardInput.WriteLine("q");

                if (!_currentProcess.WaitForExit(10000))
                {
                    _logger.LogWarning("Le processus n'a pas répondu au signal d'arrêt, tentative de terminaison forcée");
                    _currentProcess.Kill();
                }

                _currentProcess.Dispose();
                _currentProcess = null;

                _logger.LogInformation("Enregistrement arrêté avec succès");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'arrêt de l'enregistrement");
                return false;
            }
        }

        public async Task<bool> ConvertToMp4(string tsFile)
        {
            try
            {
                string ffmpegPath = _executableLocator.FindExecutable("ffmpeg");
                string mp4File = Path.ChangeExtension(tsFile, ".mp4");

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = ffmpegPath,
                        Arguments = $"-i \"{tsFile}\" -c copy \"{mp4File}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                await process.WaitForExitAsync();

                if (process.ExitCode == 0)
                {
                    File.Delete(tsFile);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la conversion");
                return false;
            }
        }

        public async Task<bool> RecordStream(string url, string outputPath, string platform)
        {
            try
            {
                var streamlinkPath = _executableLocator.FindExecutable("streamlink");
                if (string.IsNullOrEmpty(streamlinkPath))
                {
                    _logger.LogError("Streamlink non trouvé");
                    return false;
                }

                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = streamlinkPath,
                    Arguments = $"\"{url}\" best -o \"{outputPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(startInfo);
                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    _logger.LogError($"Erreur lors de l'enregistrement du stream : {error}");
                    return false;
                }

                await _statsLogger.LogRecordingStatsAsync(url, outputPath, platform);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'enregistrement du stream");
                return false;
            }
        }

        public async Task<bool> ConvertRecording(string inputPath)
        {
            try
            {
                var outputPath = inputPath.Replace(".ts", ".mp4");
                return await _videoConverter.ConvertVideoAsync(inputPath, outputPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la conversion de l'enregistrement");
                return false;
            }
        }
    }
}

