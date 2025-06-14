using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Linq;
using Strivea.Models;
using System.IO;
using System.Diagnostics;

namespace Strivea.Services
{
    public class StreamDetector : IStreamDetector
    {
        private readonly ILogger<StreamDetector> _logger;
        private readonly IExecutableLocator _executableLocator;

        public StreamDetector(ILogger<StreamDetector> logger, IExecutableLocator executableLocator)
        {
            _logger = logger;
            _executableLocator = executableLocator;
        }

        public bool CanHandle(string url)
        {
            return !string.IsNullOrEmpty(url) && 
                   (url.Contains("youtube.com") || 
                    url.Contains("twitch.tv") || 
                    url.Contains("kick.com") ||
                    url.Contains("youtu.be"));
        }

        public async Task<bool> IsLiveAsync(string url)
        {
            try
            {
                _logger.LogInformation($"Vérification du statut en direct pour {url}");
                var startInfo = new ProcessStartInfo
                {
                    FileName = "streamlink",
                    Arguments = $"--stream-url {url} best",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = startInfo };
                process.Start();
                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                _logger.LogInformation($"Sortie streamlink : {output}");
                if (!string.IsNullOrEmpty(error))
                {
                    _logger.LogError($"Erreur streamlink : {error}");
                    return false;
                }

                var isLive = output.Contains("live=1") || 
                             output.Contains("playlist_type/LIVE") || 
                             output.Contains("live=yes") ||
                             output.Contains("live=true");

                _logger.LogInformation($"Stream en direct détecté : {isLive} (URL: {output})");
                return isLive;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur lors de la vérification du statut en direct : {ex.Message}");
                return false;
            }
        }

        public async Task<string> GetStreamUrlAsync(string url)
        {
            try
            {
                _logger.LogInformation($"Récupération de l'URL du stream pour : {url}");
                
                var streamlinkPath = _executableLocator.FindExecutable("streamlink");
                if (string.IsNullOrEmpty(streamlinkPath))
                {
                    _logger.LogError("Streamlink non trouvé");
                    throw new Exception("Streamlink non trouvé");
                }

                // TODO: Implémenter la récupération réelle avec streamlink
                // Pour l'instant, on simule une URL
                await Task.Delay(1000);
                return url;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erreur lors de la récupération de l'URL du stream pour {url}");
                throw;
            }
        }

        public async Task<StreamInfo> GetStreamInfoAsync(string url)
        {
            try
            {
                _logger.LogInformation($"Récupération des informations du stream pour : {url}");
                
                var isLive = await IsLiveAsync(url);
                var streamUrl = await GetStreamUrlAsync(url);

                return new StreamInfo
                {
                    ChannelName = url.Split('/').Last(),
                    StreamUrl = streamUrl,
                    Title = "Stream en direct",
                    IsLive = isLive
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erreur lors de la récupération des informations du stream pour {url}");
                throw;
            }
        }

        private async Task StartRecordingAsync(StreamInfo stream)
        {
            try
            {
                var platform = stream.Platform.ToLower();
                var streamerName = stream.StreamerName;
                var streamTitle = stream.StreamTitle ?? "stream_sans_titre";
                
                // Nettoyer le titre pour le nom de fichier
                streamTitle = string.Join("_", streamTitle.Split(Path.GetInvalidFileNameChars()));
                
                var recordingPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "Strivea",
                    "Recordings",
                    platform,
                    streamerName,
                    $"{streamTitle}.ts"
                );

                Directory.CreateDirectory(Path.GetDirectoryName(recordingPath));
                _logger.LogInformation($"Démarrage de l'enregistrement vers : {recordingPath}");

                var startInfo = new ProcessStartInfo
                {
                    FileName = "streamlink",
                    Arguments = $"{stream.Url} best -o \"{recordingPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = startInfo };
                process.Start();

                // Démarrer la conversion en MP4 en arrière-plan
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // Attendre que le fichier .ts soit créé
                        while (!File.Exists(recordingPath))
                        {
                            await Task.Delay(1000);
                        }

                        var mp4Path = Path.ChangeExtension(recordingPath, ".mp4");
                        _logger.LogInformation($"Démarrage de la conversion vers : {mp4Path}");

                        var ffmpegStartInfo = new ProcessStartInfo
                        {
                            FileName = "ffmpeg",
                            Arguments = $"-i \"{recordingPath}\" -c copy \"{mp4Path}\"",
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };

                        using var ffmpegProcess = new Process { StartInfo = ffmpegStartInfo };
                        ffmpegProcess.Start();
                        await ffmpegProcess.WaitForExitAsync();

                        if (ffmpegProcess.ExitCode == 0)
                        {
                            _logger.LogInformation("Conversion en MP4 terminée avec succès");
                            // Supprimer le fichier .ts original
                            File.Delete(recordingPath);
                        }
                        else
                        {
                            _logger.LogError("Erreur lors de la conversion en MP4");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Erreur lors de la conversion : {ex.Message}");
                    }
                });

                stream.IsRecording = true;
                _logger.LogInformation($"Enregistrement démarré pour {stream.StreamerName}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur lors du démarrage de l'enregistrement : {ex.Message}");
            }
        }
    }
} 