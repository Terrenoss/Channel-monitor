using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Strivea.Models;
using System.Linq;
using System.Text.RegularExpressions;

namespace Strivea.Services
{
    public interface IStreamRecorder
    {
        Task StartRecordingAsync(string streamUrl, string platform, string channelName, string streamTitle, CancellationToken cancellationToken);
        Task StopRecordingAsync();
        bool IsRecording { get; }
    }

    public class StreamRecorder : IStreamRecorder
    {
        private readonly ILogger<StreamRecorder> _logger;
        private Process _recordingProcess;
        private bool _isRecording;
        private readonly IExecutableLocator _executableLocator;
        private readonly RecordingStatsLogger _statsLogger;
        private string _currentOutputPath;
        private DateTime _startTime;
        private CancellationTokenSource _cancellationTokenSource;
        private string _platform;
        private string _channelName;
        private string _streamTitle;

        public StreamRecorder(
            ILogger<StreamRecorder> logger,
            IExecutableLocator executableLocator,
            RecordingStatsLogger statsLogger)
        {
            _logger = logger;
            _executableLocator = executableLocator;
            _statsLogger = statsLogger;
        }

        public bool IsRecording => _isRecording;

        private string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return "untitled";

            // Remplacer les caractères invalides par des underscores
            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = invalidChars.Aggregate(fileName, (current, invalidChar) => current.Replace(invalidChar, '_'));

            // Supprimer les caractères non-ASCII
            sanitized = Regex.Replace(sanitized, @"[^\x20-\x7E]", "");

            // Limiter la longueur
            return sanitized.Length > 100 ? sanitized.Substring(0, 100) : sanitized;
        }

        public async Task StartRecordingAsync(string streamUrl, string platform, string channelName, string streamTitle, CancellationToken cancellationToken)
        {
            try
            {
                if (_isRecording)
                {
                    _logger.LogWarning("Un enregistrement est déjà en cours");
                    return;
                }

                if (string.IsNullOrEmpty(streamUrl))
                    throw new ArgumentNullException(nameof(streamUrl), "L'URL du stream ne peut pas être nulle");
                if (string.IsNullOrEmpty(platform))
                    throw new ArgumentNullException(nameof(platform), "La plateforme ne peut pas être nulle");
                if (string.IsNullOrEmpty(channelName))
                    throw new ArgumentNullException(nameof(channelName), "Le nom de la chaîne ne peut pas être nul");
                if (string.IsNullOrEmpty(streamTitle))
                    throw new ArgumentNullException(nameof(streamTitle), "Le titre du stream ne peut pas être nul");

                _platform = platform;
                _channelName = SanitizeFileName(channelName);
                _streamTitle = SanitizeFileName(streamTitle);

                // Créer le chemin d'enregistrement
                var baseDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "Strivea",
                    "Recordings",
                    SanitizeFileName(platform),
                    _channelName);

                _logger.LogInformation($"Création du répertoire : {baseDir}");

                // Créer le répertoire s'il n'existe pas
                if (!Directory.Exists(baseDir))
                {
                    Directory.CreateDirectory(baseDir);
                }

                // Créer le nom du fichier
                var fileName = $"{_streamTitle}.ts";
                var outputPath = Path.Combine(baseDir, fileName);

                _logger.LogInformation($"Démarrage de l'enregistrement pour {_channelName} sur {_platform}");
                _logger.LogInformation($"URL du stream : {streamUrl}");
                _logger.LogInformation($"Chemin de sortie : {outputPath}");

                var streamlinkPath = _executableLocator.FindExecutable("streamlink");
                if (string.IsNullOrEmpty(streamlinkPath))
                {
                    _logger.LogError("Streamlink non trouvé");
                    throw new Exception("Streamlink non trouvé");
                }

                _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                var startInfo = new ProcessStartInfo
                {
                    FileName = streamlinkPath,
                    Arguments = $"{streamUrl} best -o \"{outputPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                _recordingProcess = new Process { StartInfo = startInfo };
                _recordingProcess.Start();
                _isRecording = true;
                _startTime = DateTime.Now;
                _currentOutputPath = outputPath;

                _logger.LogInformation($"Enregistrement démarré pour {_channelName} sur {_platform}");

                // L'attente du processus sera gérée par l'appelant ou un mécanisme de surveillance séparé
                // ou lors de l'arrêt explicite par l'utilisateur via StopRecordingAsync.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors du démarrage de l'enregistrement");
                throw;
            }
        }

        public async Task StopRecordingAsync()
        {
            try
            {
                if (!_isRecording || _recordingProcess == null)
                {
                    return;
                }

                _logger.LogInformation($"Arrêt de l'enregistrement pour {_channelName} sur {_platform}");
                
                // Annuler le token pour arrêter la boucle d'attente
                _cancellationTokenSource?.Cancel();

                // Tuer le processus d'enregistrement
                if (!_recordingProcess.HasExited)
                {
                    _recordingProcess.Kill();
                    await _recordingProcess.WaitForExitAsync();
                }

                _isRecording = false;

                if (!string.IsNullOrEmpty(_currentOutputPath) && File.Exists(_currentOutputPath))
                {
                    var duration = DateTime.Now - _startTime;
                    var fileSize = new FileInfo(_currentOutputPath).Length;
                    _statsLogger.Log(_startTime, fileSize);

                    _logger.LogInformation($"Enregistrement terminé pour {_channelName} sur {_platform}");
                    _logger.LogInformation($"Durée : {duration:hh\\:mm\\:ss}");
                    _logger.LogInformation($"Taille du fichier : {fileSize / 1024 / 1024} MB");

                    // Convertir automatiquement le fichier
                    await ConvertRecordingAsync(_currentOutputPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'arrêt de l'enregistrement");
            }
            finally
            {
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        private async Task ConvertRecordingAsync(string inputPath)
        {
            try
            {
                _logger.LogInformation($"Début de la conversion pour {_channelName} sur {_platform}");
                var ffmpegPath = _executableLocator.FindExecutable("ffmpeg");
                _logger.LogInformation($"Chemin de FFmpeg trouvé : {ffmpegPath}");
                
                if (string.IsNullOrEmpty(ffmpegPath))
                {
                    _logger.LogError("FFmpeg non trouvé pour la conversion");
                    return;
                }

                var outputPath = Path.ChangeExtension(inputPath, ".mp4");
                _logger.LogInformation($"Chemin de sortie pour la conversion : {outputPath}");
                
                var startInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = $"-i \"{inputPath}\" -c:v copy -c:a copy \"{outputPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                _logger.LogInformation($"Commande FFmpeg : {startInfo.FileName} {startInfo.Arguments}");

                using var process = new Process { StartInfo = startInfo };
                process.Start();
                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode == 0)
                {
                    _logger.LogInformation($"Conversion réussie pour {_channelName} sur {_platform}");
                    _logger.LogInformation($"Fichier converti : {outputPath}");
                    // Supprimer le fichier original
                    File.Delete(inputPath);
                    _logger.LogInformation($"Fichier original supprimé : {inputPath}");
                }
                else
                {
                    _logger.LogError($"Échec de la conversion pour {_channelName} sur {_platform}, code de sortie : {process.ExitCode}");
                    _logger.LogError($"Sortie standard : {output}");
                    _logger.LogError($"Erreur : {error}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la conversion du fichier");
            }
        }
    }
} 