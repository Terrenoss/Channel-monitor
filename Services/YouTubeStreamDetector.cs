using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Strivea.Models;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Net.Http;
using System.IO;

namespace Strivea.Services
{
    public class YouTubeStreamDetector : BaseStreamDetector
    {
        private readonly HttpClient _httpClient;
        private const string YOUTUBE_API_KEY = "AIzaSyD_QDMrxLrUXp4QxcZLINJPB5n8d62cemA";

        public YouTubeStreamDetector(ILogger logger, IExecutableLocator executableLocator)
            : base(logger, executableLocator)
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Strivea/1.0");
            _logger.LogInformation("YouTubeStreamDetector initialisé");
        }

        public override bool CanHandle(string url)
        {
            _logger.LogInformation($"Vérification si l'URL peut être gérée : {url}");
            var canHandle = !string.IsNullOrEmpty(url) && 
                           (url.Contains("youtube.com") || url.Contains("youtu.be"));
            _logger.LogInformation($"L'URL peut être gérée : {canHandle}");
            return canHandle;
        }

        public override async Task<bool> IsLiveAsync(string url)
        {
            try
            {
                _logger.LogInformation($"Vérification du statut en direct pour {url}");
                var info = await GetStreamInfoAsync(url);
                return info.IsLive;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erreur lors de la vérification du statut en direct pour {url}");
                return false;
            }
        }

        public async Task<string> GetStreamUrlAsync(string channelUrl)
        {
            try
            {
                _logger.LogInformation($"Récupération de l'URL du stream pour {channelUrl}");
                var streamlinkPath = _executableLocator.FindExecutable("streamlink");
                if (string.IsNullOrEmpty(streamlinkPath))
                {
                    _logger.LogError("Streamlink non trouvé");
                    return null;
                }

                _logger.LogInformation($"Chemin de streamlink : {streamlinkPath}");
                _logger.LogInformation($"Début de la vérification du stream pour l'URL : {channelUrl}");

                var startInfo = new ProcessStartInfo
                {
                    FileName = streamlinkPath,
                    Arguments = $"--stream-url {channelUrl} best",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                _logger.LogInformation($"Exécution de streamlink avec les arguments : --stream-url {channelUrl} best");

                using var process = new Process { StartInfo = startInfo };
                process.Start();
                _logger.LogInformation("Processus streamlink démarré");

                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                _logger.LogInformation($"Processus streamlink terminé avec le code : {process.ExitCode}");
                if (!string.IsNullOrEmpty(output))
                {
                    _logger.LogInformation("URL du stream récupérée avec succès");
                }
                if (!string.IsNullOrEmpty(error))
                {
                    _logger.LogInformation($"Erreur streamlink : {error}");
                }

                return process.ExitCode == 0 ? output.Trim() : null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la récupération de l'URL du stream");
                return null;
            }
        }

        private string ExtractChannelId(string url)
        {
            // /channel/UCxxxxxx
            var match = Regex.Match(url, @"/channel/([\w-]+)");
            if (match.Success)
                return match.Groups[1].Value;
            // /@handle
            match = Regex.Match(url, @"/@([\w-]+)");
            if (match.Success)
                {
                var handle = match.Groups[1].Value;
                // Utiliser l'API pour convertir le handle en channelId
                    var apiUrl = $"https://www.googleapis.com/youtube/v3/search?part=snippet&q=@{handle}&type=channel&key={YOUTUBE_API_KEY}";
                    var response = _httpClient.GetStringAsync(apiUrl).GetAwaiter().GetResult();
                    var jsonDoc = JsonDocument.Parse(response);
                var items = jsonDoc.RootElement.GetProperty("items");
                if (items.GetArrayLength() > 0)
                    {
                    return items[0].GetProperty("id").GetProperty("channelId").GetString();
                }
            }
                return null;
        }

        private async Task<bool> CheckStreamStatusAsync(string url, string streamlinkPath)
        {
            try
            {
                _logger.LogInformation($"Début de la vérification du stream pour l'URL : {url}");
                
                // Utiliser le chemin complet de streamlink
                streamlinkPath = @"C:\Program Files\Streamlink\bin\streamlink.exe";
                _logger.LogInformation($"Chemin de streamlink : {streamlinkPath}");

                if (!System.IO.File.Exists(streamlinkPath))
                {
                    _logger.LogError($"Streamlink n'existe pas à l'emplacement : {streamlinkPath}");
                    return false;
                }

                // Utiliser --stream-url pour vérifier si le stream est disponible
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = streamlinkPath,
                        Arguments = $"--stream-url {url} best",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        WorkingDirectory = Path.GetDirectoryName(streamlinkPath)
                    }
                };

                _logger.LogInformation($"Exécution de streamlink avec les arguments : {process.StartInfo.Arguments}");
                
                try
                {
                    process.Start();
                    _logger.LogInformation("Processus streamlink démarré");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erreur lors du démarrage du processus streamlink");
                    return false;
                }

                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                
                try
                {
                    await process.WaitForExitAsync();
                    _logger.LogInformation($"Processus streamlink terminé avec le code : {process.ExitCode}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erreur lors de l'attente de la fin du processus streamlink");
                    return false;
                }

                _logger.LogInformation($"Sortie streamlink : {output}");
                _logger.LogInformation($"Erreur streamlink : {error}");

                // Vérifier si nous avons un manifeste HLS ou une URL de stream
                bool isLive = !string.IsNullOrEmpty(output) && 
                             (output.Contains("manifest.googlevideo.com") || 
                              output.Contains("index.m3u8") || 
                              (output.StartsWith("http") && !error.Contains("No playable streams found")));

                _logger.LogInformation($"Stream en direct détecté : {isLive}");
                return isLive;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la vérification du statut du stream");
                return false;
            }
        }

        public override async Task<StreamInfo> GetStreamInfoAsync(string url)
        {
            string channelFolderName = null;
            string channelHandle = null;
            string streamId = null;
            try
            {
                _logger.LogInformation($"Début de la récupération des informations du stream YouTube pour : {url}");

                string videoId = ExtractVideoId(url);
                string channelId = null;
                string title = null;
                string channelName = null;
                bool isLive = false;
                bool apiLiveFound = false;

                // Si pas d'ID vidéo, on tente de trouver la vidéo live en cours sur la chaîne
                if (string.IsNullOrEmpty(videoId))
                {
                    channelId = ExtractChannelId(url);
                    if (!string.IsNullOrEmpty(channelId))
                    {
                        var searchApiUrl = $"https://www.googleapis.com/youtube/v3/search?part=snippet&channelId={channelId}&eventType=live&type=video&key={YOUTUBE_API_KEY}";
                        var searchResponse = await _httpClient.GetStringAsync(searchApiUrl);
                        _logger.LogInformation($"[API] searchResponse: {searchResponse}");
                        var searchJson = JsonDocument.Parse(searchResponse);
                        var searchItems = searchJson.RootElement.GetProperty("items");
                        if (searchItems.GetArrayLength() > 0)
                        {
                            videoId = searchItems[0].GetProperty("id").GetProperty("videoId").GetString();
                            apiLiveFound = true;
                        }
                    }
                }

                if (!string.IsNullOrEmpty(videoId))
                {
                    streamId = videoId;
                    var apiUrl = $"https://www.googleapis.com/youtube/v3/videos?part=snippet,liveStreamingDetails&id={videoId}&key={YOUTUBE_API_KEY}";
                    var response = await _httpClient.GetStringAsync(apiUrl);
                    _logger.LogInformation($"[API] videoResponse: {response}");
                    var jsonDoc = JsonDocument.Parse(response);
                    var items = jsonDoc.RootElement.GetProperty("items");
                    if (items.GetArrayLength() > 0)
                    {
                        var snippet = items[0].GetProperty("snippet");
                        title = snippet.GetProperty("title").GetString();
                        channelId = snippet.GetProperty("channelId").GetString();
                        if (snippet.TryGetProperty("customUrl", out var customUrlProp))
                        {
                            channelHandle = "@" + customUrlProp.GetString();
                        }
                        isLive = items[0].TryGetProperty("liveStreamingDetails", out var liveDetails) && liveDetails.TryGetProperty("actualStartTime", out _);
                        apiLiveFound = isLive;
                    }
                }

                if (!string.IsNullOrEmpty(channelId))
                {
                    var apiUrl = $"https://www.googleapis.com/youtube/v3/channels?part=snippet&id={channelId}&key={YOUTUBE_API_KEY}";
                    var response = await _httpClient.GetStringAsync(apiUrl);
                    _logger.LogInformation($"[API] channelResponse: {response}");
                    var jsonDoc = JsonDocument.Parse(response);
                    var items = jsonDoc.RootElement.GetProperty("items");
                    if (items.GetArrayLength() > 0)
                    {
                        var snippet = items[0].GetProperty("snippet");
                        channelName = snippet.GetProperty("title").GetString();
                        try
                        {
                            if (string.IsNullOrEmpty(channelHandle) && snippet.TryGetProperty("customUrl", out var customUrlProp))
                            {
                                channelHandle = "@" + customUrlProp.GetString();
                            }
                        }
                        catch { /* ignore si customUrl absent */ }
                    }
                }

                // Détermination du nom de dossier pour la chaîne : toujours le nom joli si possible
                var handleMatch = Regex.Match(url, @"/@([\w-]+)");
                if (!string.IsNullOrEmpty(channelName))
                {
                    channelFolderName = channelName;
                }
                else if (!string.IsNullOrEmpty(channelHandle))
                {
                    channelFolderName = "@" + channelHandle.TrimStart('@');
                }
                else if (handleMatch.Success)
                {
                    channelFolderName = "@" + handleMatch.Groups[1].Value.TrimStart('@');
                }
                else
                {
                    channelFolderName = url.Split('/').Last();
                }

                if (string.IsNullOrEmpty(title))
                    title = "Stream YouTube en direct";
                if (string.IsNullOrEmpty(channelName))
                    channelName = url.Split('/').Last();

                // --- Détection via Streamlink ---
                var streamlinkPath = _executableLocator.FindExecutable("streamlink");
                bool streamlinkIsLive = false;
                string streamUrl = url;
                if (!string.IsNullOrEmpty(streamlinkPath))
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = streamlinkPath,
                        Arguments = $"-j {url} best",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    _logger.LogInformation($"Exécution de streamlink avec les arguments : {startInfo.Arguments}");

                    using var process = new Process { StartInfo = startInfo };
                    process.Start();
                    _logger.LogInformation("Processus streamlink démarré");

                    var output = await process.StandardOutput.ReadToEndAsync();
                    var error = await process.StandardError.ReadToEndAsync();
                    await process.WaitForExitAsync();

                    _logger.LogInformation($"Processus streamlink terminé avec le code : {process.ExitCode}");
                    if (!string.IsNullOrEmpty(output))
                    {
                        _logger.LogInformation($"Sortie Streamlink (JSON) : {output}");
                    }
                    if (!string.IsNullOrEmpty(error))
                    {
                        _logger.LogWarning($"Erreur Streamlink : {error}");
                    }

                    if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
                    {
                        try
                        {
                            using JsonDocument doc = JsonDocument.Parse(output);
                            JsonElement root = doc.RootElement;
                            if (root.TryGetProperty("url", out var urlElement))
                            {
                                streamUrl = urlElement.GetString();
                                if (streamUrl.Contains("live=1") || streamUrl.Contains("playlist_type/LIVE") || streamUrl.Contains("yt_live_broadcast"))
                                    streamlinkIsLive = true;
                            }
                        }
                        catch (JsonException) { }
                    }
                }

                // On considère le stream comme live si l'un des deux le dit
                isLive = apiLiveFound || streamlinkIsLive;

                return new StreamInfo
                {
                    ChannelName = channelName,
                    StreamUrl = streamUrl,
                    Title = title,
                    StreamTitle = title,
                    IsLive = isLive,
                    Platform = "YouTube",
                    ChannelFolderName = channelFolderName,
                    StreamId = streamId,
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erreur inattendue lors de la récupération des informations du stream pour {url}");
                return new StreamInfo
                {
                    ChannelName = url.Split('/')[^1],
                    StreamUrl = url,
                    Title = "Erreur de récupération d'informations",
                    StreamTitle = "Erreur de récupération d'informations",
                    IsLive = false,
                    Platform = "YouTube",
                    ChannelFolderName = channelFolderName,
                    StreamId = streamId
                };
            }
        }

        // Méthode utilitaire pour extraire l'ID vidéo d'une URL YouTube
        private string ExtractVideoId(string url)
        {
            // Gère les formats d'URL classiques
            var regex = new Regex(@"(?:v=|youtu\.be/|/live/|/shorts/|embed/)([\w-]{11})");
            var match = regex.Match(url);
            if (match.Success)
                return match.Groups[1].Value;
            return null;
        }
    }
} 