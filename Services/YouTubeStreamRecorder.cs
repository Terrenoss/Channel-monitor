using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;

namespace AutoStreamRec.Services
{
    public class YouTubeStreamRecorder
    {
        private readonly Action<string> _log;
        private readonly FileHelper _fileHelper;
        private readonly YouTubeHelper _ytHelper;
        private readonly RecordingStatsLogger _stats;
        private readonly VideoConverter _converter;
        private readonly ExecutableLocator _locator;

        private Process _currentProcess;
        private DateTime _startTime;
        private long _bytesRecorded;
        private System.Timers.Timer _statsTimer;
        private string _lastRecordedFile;
        private string _detectedQuality;

        public YouTubeStreamRecorder(Action<string> logAction,
                                     FileHelper fileHelper,
                                     YouTubeHelper ytHelper,
                                     RecordingStatsLogger stats,
                                     VideoConverter converter,
                                     ExecutableLocator locator)
        {
            _log = logAction;
            _fileHelper = fileHelper;
            _ytHelper = ytHelper;
            _stats = stats;
            _converter = converter;
            _locator = locator;
        }

        public async Task<bool> RecordStream(string youtubeUrl, CancellationToken token)
        {
            string channelName = await _ytHelper.GetChannelNameAsync(youtubeUrl);
            string safeName = _fileHelper.SanitizeFileName(channelName);

            string outputDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                "recordings", "YouTube", safeName);

            if (!await _fileHelper.EnsureDirectoryWritableAsync(outputDir))
                return false;

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string outputFile = Path.Combine(outputDir, $"{timestamp}.ts");
            _lastRecordedFile = outputFile;

            string args = $"\"{youtubeUrl}\" best --force -o \"{outputFile}\" --retry-streams 30 --retry-max 10 --loglevel debug";
            _log($"Commande Streamlink: {args}");

            _startTime = DateTime.Now;
            _bytesRecorded = 0;

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
                _log("ERREUR: Impossible de démarrer Streamlink");
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
                            _bytesRecorded = size;
                            _stats.Log(_startTime, _bytesRecorded);
                        }
                    }
                }
                catch { }
            };
            _statsTimer.Start();

            _currentProcess.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    if (e.Data.Contains("Found matching stream:"))
                    {
                        _detectedQuality = e.Data.Split(':')[1].Trim();
                        _log($"Qualité réelle détectée : {_detectedQuality}");
                    }

                    if (!e.Data.Contains("[stream.hls][debug]"))
                    {
                        _log($"[Streamlink] {e.Data}");
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
                _log("Enregistrement annulé par l'utilisateur");
                throw;
            }
            finally
            {
                _statsTimer?.Stop();
                _statsTimer?.Dispose();
            }

            if (!File.Exists(outputFile) || new FileInfo(outputFile).Length == 0)
            {
                _log("ERREUR: Fichier de sortie invalide");
                return false;
            }

            _log("Début de la conversion en MP4...");
            return await _converter.ConvertToMp4(outputFile);
        }

        public async void Stop()
        {
            try
            {
                if (_currentProcess != null && !_currentProcess.HasExited)
                {
                    _log("Arrêt du processus Streamlink...");
                    _currentProcess.Kill();
                    _log("Processus arrêté");
                }
            }
            catch (Exception ex)
            {
                _log($"Erreur lors de l'arrêt: {ex.Message}");
            }

            try
            {
                if (!string.IsNullOrEmpty(_lastRecordedFile) && File.Exists(_lastRecordedFile))
                {
                    _log("Conversion du fichier .ts arrêté manuellement...");
                    await _converter.ConvertToMp4(_lastRecordedFile);
                }
            }
            catch (Exception ex)
            {
                _log($"Erreur lors de la conversion post-arrêt: {ex.Message}");
            }
        }
    }
}
