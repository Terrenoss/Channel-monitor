using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Strivea.Models;
using System.Linq;
using System.Text.RegularExpressions;
using System.Collections.Generic;

namespace Strivea.Services
{
    public interface IStreamRecorder : IDisposable
    {
        Task StartRecordingAsync(string streamUrl, string platform, string channelName, string streamTitle, CancellationToken cancellationToken, string channelFolderName = null, string liveId = null);
        Task StopRecordingAsync();
        bool IsRecording { get; }
        string CurrentStreamId { get; }
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
        private string _tempDir;
        private List<string> _tsParts = new List<string>();
        private readonly Action<string> _setStatusMessage;
        private readonly Action<string> _addUiLog;
        private string _sessionId;
        private string _sessionTempDir;
        private bool _disposed = false;

        public StreamRecorder(
            ILogger<StreamRecorder> logger,
            IExecutableLocator executableLocator,
            RecordingStatsLogger statsLogger,
            Action<string> setStatusMessage = null,
            Action<string> addUiLog = null)
        {
            _logger = logger;
            _executableLocator = executableLocator;
            _statsLogger = statsLogger;
            _setStatusMessage = setStatusMessage;
            _addUiLog = addUiLog;
        }

        public bool IsRecording => _isRecording;
        public string ChannelName => _channelName;
        public string CurrentStreamId => _sessionId;

        public string SanitizeFileName(string fileName)
        {
            // Cette fonction NE supprime PAS les caractères non-ASCII (japonais, etc.).
            // Elle ne remplace que les caractères interdits par Windows par un underscore.
            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = invalidChars.Aggregate(fileName, (current, invalidChar) => current.Replace(invalidChar, '_'));
            // Limiter la longueur
            return sanitized.Length > 100 ? sanitized.Substring(0, 100) : sanitized;
        }

        public async Task StartRecordingAsync(string streamUrl, string platform, string channelName, string streamTitle, CancellationToken cancellationToken, string channelFolderName = null, string liveId = null)
        {
            ThrowIfDisposed();
            
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

                // Utiliser la fonction de sanitization pour tous les noms de dossier/fichier
                _platform = SanitizeFileName(platform);
                _channelName = SanitizeFileName(channelFolderName ?? channelName);
                _streamTitle = SanitizeFileName(streamTitle);

                // Créer le dossier Temp pour les morceaux .ts
                _tempDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "Strivea",
                    "Recordings",
                    _platform,
                    _channelName,
                    "Temp");
                if (!Directory.Exists(_tempDir))
                    Directory.CreateDirectory(_tempDir);

                // Générer l'identifiant de session
                if (!string.IsNullOrEmpty(liveId))
                {
                    _sessionId = liveId;
                }
                else
                {
                    _sessionId = $"{platform}_{channelName}_{DateTime.Now:yyyyMMdd_HHmmss}";
                }
                // Créer un dossier temporaire propre à la session
                _sessionTempDir = Path.Combine(_tempDir, _sessionId);
                Directory.CreateDirectory(_sessionTempDir);

                // Nom unique pour chaque morceau .ts
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var fileName = $"{_streamTitle}_{timestamp}.ts";
                var outputPath = Path.Combine(_sessionTempDir, fileName);
                _tsParts.Add(outputPath);

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
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors du démarrage de l'enregistrement");
                // Nettoyer en cas d'erreur
                await CleanupResourcesAsync();
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
                    await ConcatAndConvertTsPartsAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'arrêt de l'enregistrement");
            }
            finally
            {
                await CleanupResourcesAsync();
            }
        }

        private async Task CleanupResourcesAsync()
        {
            try
            {
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                
                if (_recordingProcess != null && !_recordingProcess.HasExited)
                {
                    try
                    {
                        _recordingProcess.Kill();
                        await _recordingProcess.WaitForExitAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Erreur lors de l'arrêt du processus d'enregistrement");
                    }
                }
                
                _recordingProcess?.Dispose();
                _recordingProcess = null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors du nettoyage des ressources");
            }
        }

        private async Task ConcatAndConvertTsPartsAsync()
        {
            _addUiLog?.Invoke("Conversion et concaténation lancées (FFmpeg) si des segments existent...");
            _setStatusMessage?.Invoke("Concaténation des segments vidéo en cours...");
            _logger.LogInformation("[CONCAT] Début de la concaténation des segments vidéo...");
            _addUiLog?.Invoke("Début de la concaténation des segments vidéo...");
            // Utiliser le dossier temporaire de la session
            var tsDir = _sessionTempDir ?? _tempDir;
            var tsFiles = Directory.GetFiles(tsDir, "*.ts")
                .Where(f => !f.EndsWith("_merged.ts") && !f.EndsWith(".mp4"))
                .OrderBy(f => new FileInfo(f).CreationTime)
                .ToList();

            if (tsFiles.Count == 0)
            {
                _logger.LogWarning($"Aucun fichier .ts trouvé dans {tsDir} pour la concaténation.");
                _setStatusMessage?.Invoke("Aucun segment vidéo à concaténer.");
                _addUiLog?.Invoke("Aucun segment vidéo à concaténer.");
                return;
            }

            // Cas 1 seul .ts : conversion directe
            if (tsFiles.Count == 1)
            {
                var tsFile = tsFiles[0];
                var singleOutputMp4 = Path.Combine(tsDir, $"{_streamTitle}.mp4");
                _setStatusMessage?.Invoke("Conversion directe du segment vidéo en mp4...");
                _logger.LogInformation($"[CONCAT] Conversion directe du segment {tsFile} en mp4...");
                _addUiLog?.Invoke("Conversion directe du segment vidéo en mp4...");
                var ffmpegArgs = $"-i \"{tsFile}\" -c:v libx264 -c:a aac \"{singleOutputMp4}\"";
                _logger.LogInformation($"Commande FFmpeg conversion directe : ffmpeg {ffmpegArgs}");
                var result = await RunFfmpegAsync(ffmpegArgs, logError:true);
                _logger.LogInformation($"FFmpeg conversion directe result: {result}");
                if (!File.Exists(singleOutputMp4))
                {
                    _logger.LogError($"La conversion FFmpeg a échoué, fichier {singleOutputMp4} non trouvé. Sortie FFmpeg : {result}");
                    _setStatusMessage?.Invoke("Erreur lors de la conversion en mp4.");
                    _addUiLog?.Invoke("Erreur lors de la conversion en mp4.");
                    return;
                }
                // Déplacement final comme avant
                var singleChannelDir = Directory.GetParent(tsDir)?.Parent?.FullName;
                if (!string.IsNullOrEmpty(singleChannelDir))
                {
                    var finalMp4 = Path.Combine(singleChannelDir, $"{_streamTitle}.mp4");
                    if (File.Exists(finalMp4))
                    {
                        _logger.LogInformation($"Suppression de l'ancien finalMp4 : {finalMp4}");
                        try { File.Delete(finalMp4); _logger.LogInformation($"Suppression réussie de {finalMp4}"); } catch (Exception ex) { _logger.LogError(ex, $"Erreur lors de la suppression de {finalMp4}"); }
                    }
                    try {
                        File.Move(singleOutputMp4, finalMp4);
                        _logger.LogInformation($"Déplacement réussi de {singleOutputMp4} vers {finalMp4}");
                    } catch (Exception ex) {
                        _logger.LogError(ex, $"Erreur lors du déplacement de {singleOutputMp4} vers {finalMp4}");
                    }
                    _logger.LogInformation($"[CONCAT] Fichier final déplacé vers : {finalMp4}");
                    _setStatusMessage?.Invoke("Conversion terminée !");
                    _addUiLog?.Invoke("Concaténation et conversion terminées !");
                    _logger.LogInformation("[CONCAT] Concaténation et conversion terminées avec succès.");
                }
                else
                {
                    _logger.LogWarning($"[CONCAT] Impossible de déplacer le .mp4 final, channelDir introuvable : {tsDir}");
                    _setStatusMessage?.Invoke("Conversion terminée, mais impossible de déplacer le fichier final.");
                    _addUiLog?.Invoke("Conversion terminée, mais impossible de déplacer le fichier final.");
                }
                return;
            }

            _logger.LogInformation($"[CONCAT] Fichiers .ts à concaténer ({tsFiles.Count}) :");
            _addUiLog?.Invoke($"Nombre de segments à concaténer : {tsFiles.Count}");
            foreach (var ts in tsFiles)
            {
                _logger.LogInformation($"[CONCAT] - {ts}");
                _addUiLog?.Invoke($"Segment : {Path.GetFileName(ts)}");
            }

            var tempConcatDir = Path.Combine(tsDir, "TempConcat");
            if (Directory.Exists(tempConcatDir))
                Directory.Delete(tempConcatDir, true);
            Directory.CreateDirectory(tempConcatDir);

            var simpleNames = new List<string>();
            for (int i = 0; i < tsFiles.Count; i++)
            {
                var simpleName = $"part{i + 1}.ts";
                var dest = Path.Combine(tempConcatDir, simpleName);
                File.Copy(tsFiles[i], dest, true);
                simpleNames.Add(simpleName);
            }

            var concatFile = Path.Combine(tempConcatDir, "concat.txt");
            var concatLines = simpleNames.Select(n => $"file '{n}'");
            await File.WriteAllLinesAsync(concatFile, concatLines, new System.Text.UTF8Encoding(false));

            var tempMp4 = Path.Combine(tempConcatDir, "output.mp4");
            var outputMp4 = Path.Combine(tsDir, $"{_streamTitle}.mp4");

            _setStatusMessage?.Invoke("Conversion en mp4 en cours...");
            _logger.LogInformation("[CONCAT] Début de la conversion en mp4...");
            _addUiLog?.Invoke("Début de la conversion en mp4...");
            var ffmpegConcatReencodeArgs = $"-f concat -safe 0 -i \"{concatFile}\" -c:v libx264 -c:a aac \"{tempMp4}\"";
            _logger.LogInformation($"Commande FFmpeg concat+reencode : ffmpeg {ffmpegConcatReencodeArgs}");
            var concatResult = await RunFfmpegAsync(ffmpegConcatReencodeArgs, logError:true);
            _logger.LogInformation($"FFmpeg concat+reencode result: {concatResult}");

            if (!File.Exists(tempMp4))
                {
                _logger.LogError($"La concaténation+réencodage FFmpeg a échoué, fichier {tempMp4} non trouvé. Sortie FFmpeg : {concatResult}");
                _setStatusMessage?.Invoke("Erreur lors de la conversion en mp4.");
                _addUiLog?.Invoke("Erreur lors de la conversion en mp4.");
                    return;
                }

            if (File.Exists(outputMp4))
            {
                _logger.LogInformation($"Suppression de l'ancien outputMp4 : {outputMp4}");
                try { File.Delete(outputMp4); _logger.LogInformation($"Suppression réussie de {outputMp4}"); } catch (Exception ex) { _logger.LogError(ex, $"Erreur lors de la suppression de {outputMp4}"); }
            }
            try {
                File.Move(tempMp4, outputMp4);
                _logger.LogInformation($"Déplacement réussi de {tempMp4} vers {outputMp4}");
            } catch (Exception ex) {
                _logger.LogError(ex, $"Erreur lors du déplacement de {tempMp4} vers {outputMp4}");
            }

            var channelDir = Directory.GetParent(tsDir)?.Parent?.FullName;
            if (!string.IsNullOrEmpty(channelDir))
            {
                var finalMp4 = Path.Combine(channelDir, $"{_streamTitle}.mp4");
                if (File.Exists(finalMp4))
                {
                    _logger.LogInformation($"Suppression de l'ancien finalMp4 : {finalMp4}");
                    try { File.Delete(finalMp4); _logger.LogInformation($"Suppression réussie de {finalMp4}"); } catch (Exception ex) { _logger.LogError(ex, $"Erreur lors de la suppression de {finalMp4}"); }
                }
                try {
                    File.Move(outputMp4, finalMp4);
                    _logger.LogInformation($"Déplacement réussi de {outputMp4} vers {finalMp4}");
                } catch (Exception ex) {
                    _logger.LogError(ex, $"Erreur lors du déplacement de {outputMp4} vers {finalMp4}");
                }
                _logger.LogInformation($"[CONCAT] Fichier final déplacé vers : {finalMp4}");
                _setStatusMessage?.Invoke("Conversion terminée !");
                _addUiLog?.Invoke("Concaténation et conversion terminées !");
                _logger.LogInformation("[CONCAT] Concaténation et conversion terminées avec succès.");
            }
            else
            {
                _logger.LogWarning($"[CONCAT] Impossible de déplacer le .mp4 final, channelDir introuvable : {tsDir}");
                _setStatusMessage?.Invoke("Conversion terminée, mais impossible de déplacer le fichier final.");
                _addUiLog?.Invoke("Conversion terminée, mais impossible de déplacer le fichier final.");
            }

            Directory.Delete(tempConcatDir, true);
        }

        private async Task<string> RunFfmpegAsync(string args, bool logError = false)
        {
            var ffmpegPath = _executableLocator.FindExecutable("ffmpeg");
            if (string.IsNullOrEmpty(ffmpegPath))
            {
                _logger.LogError("FFmpeg non trouvé");
                throw new Exception("FFmpeg non trouvé");
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (var process = new Process { StartInfo = startInfo })
            {
                var outputBuilder = new System.Text.StringBuilder();
                var errorBuilder = new System.Text.StringBuilder();
                DateTime lastOutput = DateTime.Now;
                object lockObj = new object();

                process.OutputDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                    {
                        lock (lockObj) { lastOutput = DateTime.Now; }
                        outputBuilder.AppendLine(e.Data);
                    }
                };
                process.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                    {
                        lock (lockObj) { lastOutput = DateTime.Now; }
                        errorBuilder.AppendLine(e.Data);
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                // Timeout d'inactivité (60s) et timeout global (30min)
                var inactivityTimeout = TimeSpan.FromSeconds(60);
                var globalTimeout = TimeSpan.FromMinutes(30);
                var startTime = DateTime.Now;
                
                // Utiliser un CancellationTokenSource pour gérer les timeouts
                using (var timeoutCts = new CancellationTokenSource())
                {
                    // Créer une tâche pour surveiller l'inactivité
                    var inactivityTask = Task.Run(async () =>
                    {
                        while (!timeoutCts.Token.IsCancellationRequested)
                        {
                            await Task.Delay(1000, timeoutCts.Token);
                            lock (lockObj)
                            {
                                if (DateTime.Now - lastOutput > inactivityTimeout)
                                {
                                    _logger.LogError("FFmpeg bloqué (aucune sortie depuis 60s), arrêt du process.");
                                    timeoutCts.Cancel();
                                    break;
                                }
                            }
                        }
                    }, timeoutCts.Token);

                    // Créer une tâche pour le timeout global
                    var globalTimeoutTask = Task.Delay(globalTimeout, timeoutCts.Token);

                    // Attendre que FFmpeg se termine ou qu'un timeout se déclenche
                    try
                    {
                        await Task.WhenAny(
                            process.WaitForExitAsync(),
                            globalTimeoutTask
                        );

                        if (!process.HasExited)
                        {
                            _logger.LogError("FFmpeg a dépassé le temps limite global (30min) et va être tué.");
                            process.Kill();
                            await process.WaitForExitAsync();
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Timeout d'inactivité déclenché
                        if (!process.HasExited)
                        {
                            process.Kill();
                            await process.WaitForExitAsync();
                        }
                        throw new Exception("FFmpeg bloqué (inactivité)");
                    }
                    finally
                    {
                        timeoutCts.Cancel(); // Arrêter la tâche de surveillance
                    }
                }

                // S'assurer que toute la sortie est lue
                process.WaitForExit(); // Pour vider les buffers
                process.CancelOutputRead();
                process.CancelErrorRead();

                var output = outputBuilder.ToString();
                var error = errorBuilder.ToString();

                if (logError && !string.IsNullOrWhiteSpace(error))
                {
                    _logger.LogError($"FFmpeg stderr : {error}");
                }

                _logger.LogInformation($"FFmpeg terminé avec code {process.ExitCode}");
                if (process.ExitCode != 0)
                {
                    _logger.LogError($"Erreur lors de l'exécution de FFmpeg. Sortie: {error}");
                    throw new Exception($"Erreur lors de l'exécution de FFmpeg. Sortie: {error}");
                }

                return output + (string.IsNullOrWhiteSpace(error) ? "" : "\n[stderr]\n" + error);
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(StreamRecorder));
            }
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
                    // Nettoyer les ressources de manière synchrone
                    _cancellationTokenSource?.Cancel();
                    _cancellationTokenSource?.Dispose();
                    
                    if (_recordingProcess != null && !_recordingProcess.HasExited)
                    {
                        try
                        {
                            _recordingProcess.Kill();
                            _recordingProcess.WaitForExit(5000); // 5 secondes max
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogWarning(ex, "Erreur lors de l'arrêt du processus d'enregistrement lors du dispose");
                        }
                    }
                    
                    _recordingProcess?.Dispose();
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Erreur lors du dispose de StreamRecorder");
                }
                finally
                {
                    _disposed = true;
                }
            }
        }

        ~StreamRecorder()
        {
            Dispose(false);
        }
    }
} 