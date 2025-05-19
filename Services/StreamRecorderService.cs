using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace AutoStreamRec.Services
{
    public class StreamRecorderService
    {
        private readonly string recordingsDir;
        private readonly Action<string> _logAction;
        private readonly Action<string> _statAction;
        private Process _currentStreamlinkProcess;
        private DateTime _recordingStartTime;
        private long _bytesRecorded;

        public StreamRecorderService(Action<string> logAction, Action<string> statAction)
        {
            _logAction = logAction;
            _statAction = statAction;
            
            string myVideosPath = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            _logAction($"Chemin MyVideos: {myVideosPath}");
            
            if (string.IsNullOrEmpty(myVideosPath)) 
            {
                myVideosPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Videos");
            }
            
            recordingsDir = Path.Combine(myVideosPath, "recordings");
            Directory.CreateDirectory(recordingsDir);
            _logAction($"Dossier d'enregistrement: {recordingsDir}");
        }

        public async Task<bool> CheckDependencies()
        {
            _logAction("Vérification des dépendances...");
            
            var dependencies = new Dictionary<string, string>
            {
                { "streamlink", "Streamlink (pip install streamlink)" },
                { "ffmpeg", "FFmpeg (https://ffmpeg.org/)" }
            };

            bool allOk = true;
            
            foreach (var dep in dependencies)
            {
                string path = FindExecutablePath(dep.Key);
                if (string.IsNullOrEmpty(path))
                {
                    _logAction($"ERREUR: {dep.Value} non trouvé");
                    allOk = false;
                }
                else
                {
                    _logAction($"OK: {dep.Key} trouvé à {path}");
                }
            }

            return allOk;
        }

        public async Task<string> GetLiveStreamUrl(string channelUrl)
        {
            _logAction($"Détection de live pour: {channelUrl}");
            
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "streamlink",
                    Arguments = $"--json \"{channelUrl}\" best",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };

                using var process = new Process { StartInfo = startInfo };
                var output = new StringBuilder();

                process.Start();
                string jsonContent = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                _logAction($"Réponse JSON brute: {jsonContent}");

                try
                {
                    using JsonDocument doc = JsonDocument.Parse(jsonContent);
                    
                    if (doc.RootElement.TryGetProperty("type", out var typeProp) && 
                        typeProp.GetString() == "hls")
                    {
                        _logAction("Stream HLS valide détecté");
                        return "best";
                    }

                    if (doc.RootElement.TryGetProperty("streams", out var streams))
                    {
                        foreach (var stream in streams.EnumerateObject())
                        {
                            return stream.Name;
                        }
                    }
                }
                catch (JsonException jsonEx)
                {
                    _logAction($"ERREUR Parsing JSON: {jsonEx.Message}");
                    _logAction($"Contenu JSON problématique: {jsonContent}");
                }

                return null;
            }
            catch (Exception ex)
            {
                _logAction($"ERREUR Détection: {ex.Message}");
                return null;
            }
        }

        public async Task<bool> RecordYouTubeStream(string youtubeUrl, CancellationToken cancellationToken)
        {
            try
            {
                _logAction($"Initialisation enregistrement pour: {youtubeUrl}");
                
                string channelName = await GetChannelName(youtubeUrl);
                string sanitizedChannelName = SanitizeFileName(channelName);
                string outputDir = Path.Combine(recordingsDir, "YouTube", sanitizedChannelName);

                _logAction($"Tentative de création du dossier: {outputDir}");
                try
                {
                    Directory.CreateDirectory(outputDir);
                    
                    if (!Directory.Exists(outputDir))
                    {
                        _logAction("ERREUR: Le dossier n'a pas été créé");
                        return false;
                    }
                    
                    string testFile = Path.Combine(outputDir, "write_test.tmp");
                    await File.WriteAllTextAsync(testFile, "test");
                    File.Delete(testFile);
                    
                    _logAction("Dossier et permissions OK");
                }
                catch (Exception ex)
                {
                    _logAction($"ERREUR Dossier: {ex.Message}");
                    _logAction($"Chemin complet tenté: {Path.GetFullPath(outputDir)}");
                    return false;
                }

                string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                string outputFile = Path.Combine(outputDir, $"{timestamp}.ts");
                
                _logAction($"Fichier de sortie: {outputFile}");

                string arguments = $"\"{youtubeUrl}\" best --force -o \"{outputFile}\" --retry-streams 30 --retry-max 10 --loglevel debug";
                _logAction($"Commande Streamlink: {arguments}");

                _recordingStartTime = DateTime.Now;
                _bytesRecorded = 0;

                var startInfo = new ProcessStartInfo
                {
                    FileName = FindExecutablePath("streamlink"),
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                _logAction("Démarrage du processus Streamlink...");
                _currentStreamlinkProcess = Process.Start(startInfo);
                
                if (_currentStreamlinkProcess == null)
                {
                    _logAction("ERREUR: Impossible de démarrer Streamlink");
                    return false;
                }

                _logAction("Processus Streamlink démarré avec succès");

                _currentStreamlinkProcess.OutputDataReceived += (sender, args) => 
                {
                    if (!string.IsNullOrEmpty(args.Data))
                    {
                        // Filtre des logs techniques
                        bool shouldLog = !(args.Data.Contains("[stream.hls][debug] Segment") ||
                                         args.Data.Contains("[stream.hls][debug] Writing") ||
                                         args.Data.Contains("[stream.hls][debug] Adding") ||
                                         args.Data.Contains("[stream.hls][debug] Reloading"));

                        if (shouldLog && (args.Data.Contains("[cli][info]") || 
                                        args.Data.Contains("[download]") ||
                                        args.Data.Contains("error", StringComparison.OrdinalIgnoreCase)))
                        {
                            _logAction($"[Streamlink] {args.Data}");
                        }
                        
                        if (File.Exists(outputFile))
                        {
                            long newSize = new FileInfo(outputFile).Length;
                            if (newSize > _bytesRecorded)
                            {
                                _bytesRecorded = newSize;
                                LogRecordingStats(_recordingStartTime, _bytesRecorded);
                            }
                        }
                    }
                };
                
                _currentStreamlinkProcess.ErrorDataReceived += (sender, args) => 
                {
                    if (!string.IsNullOrEmpty(args.Data))
                        _logAction($"[Streamlink-ERROR] {args.Data}");
                };
                
                _currentStreamlinkProcess.BeginOutputReadLine();
                _currentStreamlinkProcess.BeginErrorReadLine();

                _logAction("Attente de la fin de l'enregistrement...");
                await _currentStreamlinkProcess.WaitForExitAsync(cancellationToken);
                
                _logAction($"Processus Streamlink terminé - Code de sortie: {_currentStreamlinkProcess.ExitCode}");

                if (_currentStreamlinkProcess.ExitCode != 0)
                {
                    _logAction($"Erreur Streamlink (code {_currentStreamlinkProcess.ExitCode})");
                    return false;
                }

                if (!File.Exists(outputFile))
                {
                    _logAction("ERREUR: Aucun fichier de sortie créé");
                    return false;
                }

                long fileSize = new FileInfo(outputFile).Length;
                if (fileSize == 0)
                {
                    _logAction("ERREUR: Fichier de sortie vide");
                    File.Delete(outputFile);
                    return false;
                }

                _logAction($"Enregistrement terminé avec succès - Taille du fichier: {fileSize} octets");
                _logAction("Début de la conversion en MP4...");
                
                bool conversionResult = await ConvertToMp4(outputFile);
                if (conversionResult)
                {
                    _logAction("Conversion terminée avec succès");
                }
                else
                {
                    _logAction("Erreur lors de la conversion");
                }
                
                return conversionResult;
            }
            catch (OperationCanceledException)
            {
                _logAction("Enregistrement annulé par l'utilisateur");
                return false;
            }
            catch (Exception ex)
            {
                _logAction($"ERREUR Enregistrement: {ex.Message}");
                return false;
            }
            finally
            {
                _currentStreamlinkProcess?.Dispose();
                _currentStreamlinkProcess = null;
            }
        }

        private string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "Unknown_Channel";

            var invalidChars = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name);
            
            foreach (var c in invalidChars)
                sb.Replace(c, '_');
            
            return sb.ToString().Trim();
        }

        private void LogRecordingStats(DateTime startTime, long bytesRecorded)
        {
            var duration = DateTime.Now - startTime;
            double mbRecorded = bytesRecorded / (1024.0 * 1024.0);
            double mbPerMinute = duration.TotalMinutes > 0 ? mbRecorded / duration.TotalMinutes : 0;
            
            _statAction($"{duration:hh\\:mm\\:ss} | {mbRecorded:F2} MB | {mbPerMinute:F2} MB/min");
        }

        public void StopRecording()
        {
            try
            {
                if (_currentStreamlinkProcess != null && !_currentStreamlinkProcess.HasExited)
                {
                    _logAction("Arrêt du processus Streamlink...");
                    _currentStreamlinkProcess.Kill();
                    _logAction("Processus Streamlink arrêté");
                }
            }
            catch (Exception ex)
            {
                _logAction($"Erreur lors de l'arrêt: {ex.Message}");
            }
        }

        private async Task<string> GetChannelName(string youtubeUrl)
        {
            if (youtubeUrl.Contains("@"))
            {
                int atIndex = youtubeUrl.IndexOf('@');
                int nextSlash = youtubeUrl.IndexOf('/', atIndex);
                
                if (nextSlash == -1)
                    return youtubeUrl.Substring(atIndex + 1);
                    
                return youtubeUrl.Substring(atIndex + 1, nextSlash - atIndex - 1);
            }

            string ytDlpPath = FindExecutablePath("yt-dlp");
            if (!string.IsNullOrEmpty(ytDlpPath))
            {
                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = ytDlpPath,
                        Arguments = $"\"{youtubeUrl}\" --print \"%(channel)s\" --no-warnings",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var process = Process.Start(startInfo);
                    string output = await process.StandardOutput.ReadToEndAsync();
                    await process.WaitForExitAsync();

                    if (!string.IsNullOrWhiteSpace(output))
                        return output.Trim();
                }
                catch { }
            }

            return "Unknown_Channel";
        }

        public async Task<bool> ConvertToMp4(string tsFilePath, string mp4FilePath = null)
        {
            _logAction($"Début conversion: {tsFilePath}");
            
            if (!File.Exists(tsFilePath))
            {
                _logAction($"ERREUR: Fichier TS introuvable: {tsFilePath}");
                return false;
            }

            long inputSize = new FileInfo(tsFilePath).Length;
            _logAction($"Taille du fichier TS: {inputSize} octets");

            string ffmpegPath = FindExecutablePath("ffmpeg");
            if (string.IsNullOrEmpty(ffmpegPath))
            {
                _logAction("ERREUR: FFmpeg non trouvé !");
                return false;
            }

            mp4FilePath ??= Path.ChangeExtension(tsFilePath, ".mp4");
            _logAction($"Fichier MP4 de sortie: {mp4FilePath}");

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = $"-y -i \"{tsFilePath}\" -c:v copy -c:a aac -strict experimental \"{mp4FilePath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                _logAction("Démarrage de FFmpeg...");
                using var process = Process.Start(startInfo);
                
                process.OutputDataReceived += (sender, args) => 
                {
                    if (!string.IsNullOrEmpty(args.Data))
                        _logAction($"[FFmpeg] {args.Data}");
                };
                process.ErrorDataReceived += (sender, args) => 
                {
                    if (!string.IsNullOrEmpty(args.Data))
                        _logAction($"[FFmpeg] {args.Data}");
                };
                
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                
                await process.WaitForExitAsync();
                _logAction($"FFmpeg terminé - Code de sortie: {process.ExitCode}");

                if (process.ExitCode != 0)
                {
                    _logAction($"ERREUR FFmpeg (code {process.ExitCode})");
                    return false;
                }

                if (!File.Exists(mp4FilePath))
                {
                    _logAction("ERREUR: Fichier MP4 non créé");
                    return false;
                }

                long outputSize = new FileInfo(mp4FilePath).Length;
                _logAction($"Conversion réussie - Taille du MP4: {outputSize} octets");

                try 
                { 
                    File.Delete(tsFilePath);
                    _logAction("Fichier TS supprimé avec succès");
                }
                catch (Exception ex) 
                { 
                    _logAction($"AVERTISSEMENT: Impossible de supprimer le fichier TS: {ex.Message}"); 
                }
                
                return true;
            }
            catch (Exception ex)
            {
                _logAction($"ERREUR Conversion: {ex.Message}");
                return false;
            }
        }

        private string FindExecutablePath(string executable)
        {
            try
            {
                string envPath = Environment.GetEnvironmentVariable("PATH");
                if (!string.IsNullOrEmpty(envPath))
                {
                    foreach (var path in envPath.Split(Path.PathSeparator))
                    {
                        var fullPath = Path.Combine(path, executable + ".exe");
                        if (File.Exists(fullPath))
                            return fullPath;
                    }
                }

                var commonPaths = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Streamlink", "bin"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Streamlink", "bin"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python", "Python311", "Scripts"),
                    Path.Combine("C:", "ffmpeg", "bin"),
                    AppContext.BaseDirectory
                };

                foreach (var path in commonPaths)
                {
                    var fullPath = Path.Combine(path, executable + ".exe");
                    if (File.Exists(fullPath))
                        return fullPath;
                }

                try
                {
                    var proc = new Process()
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = executable,
                            Arguments = "--version",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true
                        }
                    };
                    if (proc.Start())
                    {
                        proc.Kill();
                        return executable;
                    }
                }
                catch { }
            }
            catch (Exception ex)
            {
                _logAction($"Erreur FindExecutablePath: {ex.Message}");
            }

            return null;
        }
    }
}