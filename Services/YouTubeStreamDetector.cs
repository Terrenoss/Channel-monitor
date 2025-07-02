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
using System.Collections.Concurrent;

namespace Strivea.Services
{
    public class YouTubeStreamDetector : BaseStreamDetector
    {
        private readonly HttpClient _httpClient;
        private const string YOUTUBE_API_KEY = "AIzaSyD_QDMrxLrUXp4QxcZLINJPB5n8d62cemA";
        private static readonly ConcurrentDictionary<string, string> _apiCache = new();

        public YouTubeStreamDetector(ILogger logger, IExecutableLocator executableLocator)
            : base(logger, executableLocator)
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/134.0.0.0 Safari/537.36");
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
            _httpClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
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

                _logger.LogInformation($"[DIAG] Processus streamlink terminé avec le code : {process.ExitCode}");
                if (!string.IsNullOrEmpty(output))
                {
                    _logger.LogInformation($"[DIAG] Sortie Streamlink (JSON) : {output}");
                }
                if (!string.IsNullOrEmpty(error))
                {
                    _logger.LogWarning($"[DIAG] Erreur Streamlink : {error}");
                }

                return process.ExitCode == 0 ? output.Trim() : null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la récupération de l'URL du stream");
                return null;
            }
        }

        private async Task<string> ExtractChannelIdAsync(string url)
        {
            try
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
                    _logger.LogInformation($"Handle détecté : {handle}");
                    
                    // Utiliser l'API pour convertir le handle en channelId
                    var apiUrl = $"https://www.googleapis.com/youtube/v3/search?part=snippet&q=@{handle}&type=channel&key={YOUTUBE_API_KEY}";
                    _logger.LogInformation($"Appel API pour convertir handle en channelId : {apiUrl}");
                    
                    try
                    {
                        var response = await CallYouTubeApiAsync(apiUrl, $"conversion handle en channelId pour {handle}");
                        _logger.LogInformation($"[API] handleResponse: {response}");
                        
                    var jsonDoc = JsonDocument.Parse(response);
                        var items = jsonDoc.RootElement.GetProperty("items");
                        if (items.GetArrayLength() > 0)
                        {
                            var channelId = items[0].GetProperty("id").GetProperty("channelId").GetString();
                            _logger.LogInformation($"ChannelId trouvé : {channelId}");
                            return channelId;
                        }
                        else
                        {
                            _logger.LogWarning($"Aucun channelId trouvé pour le handle : {handle}");
                        }
                    }
                    catch (HttpRequestException ex) when (ex.Message.Contains("403"))
                    {
                        _logger.LogWarning($"Erreur 403 (quota dépassé) pour l'API YouTube - handle : {handle}");
                        return null;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Erreur API YouTube pour le handle {handle} : {ex.Message}");
                        return null;
                    }
                }
                
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'extraction de l'ID de la chaîne");
                return null;
            }
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

                // --- Détection via Streamlink en premier pour obtenir les métadonnées de base ---
                var streamlinkPath = _executableLocator.FindExecutable("streamlink");
                bool streamlinkIsLive = false;
                string streamUrl = url;
                string streamlinkAuthor = null;
                string streamlinkTitle = null;
                string streamlinkVideoId = null;
                
                if (!string.IsNullOrEmpty(streamlinkPath))
                {
                    _logger.LogInformation("Vérification via Streamlink...");
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

                _logger.LogInformation($"[DIAG] Processus streamlink terminé avec le code : {process.ExitCode}");
                if (!string.IsNullOrEmpty(output))
                {
                    _logger.LogInformation($"[DIAG] Sortie Streamlink (JSON) : {output}");
                }
                if (!string.IsNullOrEmpty(error))
                {
                    _logger.LogWarning($"[DIAG] Erreur Streamlink : {error}");
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
                            
                            // Extraire les métadonnées de Streamlink
                            if (root.TryGetProperty("metadata", out var metadata))
                            {
                                if (metadata.TryGetProperty("author", out var authorElement))
                                {
                                    streamlinkAuthor = authorElement.GetString();
                                    _logger.LogInformation($"Auteur détecté via Streamlink : {streamlinkAuthor}");
                                }
                                if (metadata.TryGetProperty("title", out var titleElement))
                                {
                                    streamlinkTitle = titleElement.GetString();
                                    _logger.LogInformation($"Titre détecté via Streamlink : {streamlinkTitle}");
                                }
                                if (metadata.TryGetProperty("id", out var idElement))
                                {
                                    streamlinkVideoId = idElement.GetString();
                                    _logger.LogInformation($"VideoId détecté via Streamlink : {streamlinkVideoId}");
                                }
                            }
                        }
                        catch (JsonException ex) 
                        { 
                            _logger.LogError(ex, "Erreur lors du parsing JSON de Streamlink");
                        }
                    }
                }

                // --- Logique principale : Normalisation des IDs pour la concaténation ---
                
                // Si c'est une URL de chaîne (pas d'ID vidéo), utiliser l'ID du stream actuel
                if (string.IsNullOrEmpty(videoId))
                {
                    _logger.LogInformation("URL de chaîne détectée, utilisation de l'ID du stream actuel");
                    if (!string.IsNullOrEmpty(streamlinkVideoId))
                    {
                        videoId = streamlinkVideoId;
                        streamId = streamlinkVideoId;
                        _logger.LogInformation($"Utilisation de l'ID du stream actuel via Streamlink : {streamId}");
                    }
                    else
                    {
                        _logger.LogInformation("Aucun stream actuel trouvé via Streamlink, recherche via API...");
                        try
                        {
                            channelId = await ExtractChannelIdAsync(url);
                            if (!string.IsNullOrEmpty(channelId))
                            {
                                _logger.LogInformation($"ChannelId trouvé : {channelId}, recherche des streams live...");
                                var searchApiUrl = $"https://www.googleapis.com/youtube/v3/search?part=snippet&channelId={channelId}&eventType=live&type=video&key={YOUTUBE_API_KEY}";
                                var searchResponse = await CallYouTubeApiAsync(searchApiUrl, $"recherche streams live pour channelId {channelId}");
                                _logger.LogInformation($"[API] searchResponse: {searchResponse}");
                                var searchJson = JsonDocument.Parse(searchResponse);
                                var searchItems = searchJson.RootElement.GetProperty("items");
                                if (searchItems.GetArrayLength() > 0)
                                {
                                    videoId = searchItems[0].GetProperty("id").GetProperty("videoId").GetString();
                                    streamId = videoId;
                                    _logger.LogInformation($"VideoId du stream live trouvé via API : {videoId}");
                                    apiLiveFound = true;
                                }
                                else
                                {
                                    _logger.LogInformation("Aucun stream live trouvé via l'API pour cette chaîne");
                                }
                            }
                            else
                            {
                                _logger.LogWarning("Impossible d'extraire le channelId de l'URL");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"Erreur API YouTube (ignorée) : {ex.Message}");
                        }
                    }
                }
                // Si c'est une URL directe, vérifier si elle pointe vers le stream actuel de la chaîne
                else
                {
                    _logger.LogInformation($"URL directe détectée avec videoId : {videoId}");
                    
                    // Récupérer les informations de la vidéo pour obtenir le channelId
                    try
                    {
                        var apiUrl = $"https://www.googleapis.com/youtube/v3/videos?part=snippet,liveStreamingDetails&id={videoId}&key={YOUTUBE_API_KEY}";
                        var response = await CallYouTubeApiAsync(apiUrl, $"détails vidéo pour {videoId}");
                        _logger.LogInformation($"[API] videoResponse: {response}");
                        var jsonDoc = JsonDocument.Parse(response);
                        var items = jsonDoc.RootElement.GetProperty("items");
                        if (items.GetArrayLength() > 0)
                        {
                            var snippet = items[0].GetProperty("snippet");
                            channelId = snippet.GetProperty("channelId").GetString();
                            isLive = items[0].TryGetProperty("liveStreamingDetails", out var liveDetails) && liveDetails.TryGetProperty("actualStartTime", out _);
                            apiLiveFound = isLive;
                            _logger.LogInformation($"Informations vidéo récupérées via API - ChannelId: {channelId}, IsLive: {isLive}");
                            
                            // Si c'est un stream live, vérifier s'il y a un stream plus récent sur la même chaîne
                            if (isLive)
                            {
                                _logger.LogInformation("Stream live détecté, vérification s'il y a un stream plus récent...");
                                try
                                {
                                    var searchApiUrl = $"https://www.googleapis.com/youtube/v3/search?part=snippet&channelId={channelId}&eventType=live&type=video&order=date&key={YOUTUBE_API_KEY}";
                                    var searchResponse = await CallYouTubeApiAsync(searchApiUrl, $"recherche stream plus récent pour channelId {channelId}");
                                    var searchJson = JsonDocument.Parse(searchResponse);
                                    var searchItems = searchJson.RootElement.GetProperty("items");
                                    if (searchItems.GetArrayLength() > 0)
                                    {
                                        var latestVideoId = searchItems[0].GetProperty("id").GetProperty("videoId").GetString();
                                        if (latestVideoId != videoId)
                                        {
                                            _logger.LogInformation($"Stream plus récent trouvé via API : {latestVideoId} (au lieu de {videoId})");
                                            videoId = latestVideoId;
                                            streamId = latestVideoId;
                                        }
                                        else
                                        {
                                            _logger.LogInformation("Le stream spécifié est bien le plus récent");
                                            streamId = videoId;
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning($"Erreur API YouTube pour la recherche du stream plus récent (ignorée) : {ex.Message}");
                                    
                                    // Si l'API échoue, utiliser Streamlink pour déterminer le stream le plus récent
                                    if (!string.IsNullOrEmpty(streamlinkVideoId))
                                    {
                                        _logger.LogInformation($"Utilisation de l'ID Streamlink comme stream le plus récent : {streamlinkVideoId}");
                                        videoId = streamlinkVideoId;
                                        streamId = streamlinkVideoId;
                                    }
                                    else
                                    {
                                        streamId = videoId;
                                    }
                                }
                            }
                            else
                            {
                                streamId = videoId;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Erreur API YouTube pour les détails vidéo (ignorée) : {ex.Message}");
                        
                        // Si l'API échoue, utiliser Streamlink comme fallback pour l'URL directe aussi
                        if (!string.IsNullOrEmpty(streamlinkVideoId))
                        {
                            _logger.LogInformation($"Utilisation de l'ID Streamlink comme fallback : {streamlinkVideoId}");
                            videoId = streamlinkVideoId;
                            streamId = streamlinkVideoId;
                        }
                        else
                        {
                            streamId = videoId;
                        }
                    }
                }

                // --- Récupération des informations détaillées via l'API YouTube ---
                if (!string.IsNullOrEmpty(videoId))
                {
                    try
                    {
                        var apiUrl = $"https://www.googleapis.com/youtube/v3/videos?part=snippet,liveStreamingDetails&id={videoId}&key={YOUTUBE_API_KEY}";
                        var response = await CallYouTubeApiAsync(apiUrl, $"détails vidéo pour {videoId}");
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
                            _logger.LogInformation($"Informations vidéo récupérées via API - Titre: {title}, IsLive: {isLive}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Erreur API YouTube pour les détails vidéo (ignorée) : {ex.Message}");
                    }
                }

                // --- Récupération des informations de la chaîne via l'API YouTube ---
                if (!string.IsNullOrEmpty(channelId))
                {
                    try
                    {
                        var apiUrl = $"https://www.googleapis.com/youtube/v3/channels?part=snippet&id={channelId}&key={YOUTUBE_API_KEY}";
                        var response = await CallYouTubeApiAsync(apiUrl, $"détails chaîne pour {channelId}");
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
                            _logger.LogInformation($"Informations chaîne récupérées via API - Nom: {channelName}, Handle: {channelHandle}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Erreur API YouTube pour les détails chaîne (ignorée) : {ex.Message}");
                    }
                }

                // --- Priorité aux informations de l'API YouTube, fallback sur Streamlink ---
                if (string.IsNullOrEmpty(channelName) && !string.IsNullOrEmpty(streamlinkAuthor))
                {
                    channelName = streamlinkAuthor;
                    _logger.LogInformation($"Utilisation du nom de chaîne de Streamlink (fallback) : {channelName}");
                }
                
                if (string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(streamlinkTitle))
                {
                    title = streamlinkTitle;
                    _logger.LogInformation($"Utilisation du titre de Streamlink (fallback) : {title}");
                }

                // --- Détermination du nom de dossier : toujours utiliser le nom de l'API YouTube ---
                if (!string.IsNullOrEmpty(channelName))
                {
                    channelFolderName = channelName;
                    _logger.LogInformation($"Nom de dossier final (API YouTube) : {channelFolderName}");
                }
                else
                {
                    // Fallback sur l'extraction depuis l'URL
                    var handleMatch = Regex.Match(url, @"/@([\w-]+)");
                    if (handleMatch.Success)
                    {
                        channelFolderName = "@" + handleMatch.Groups[1].Value;
                    }
                    else
                    {
                        channelFolderName = url.Split('/').Last();
                    }
                    _logger.LogInformation($"Nom de dossier fallback (URL) : {channelFolderName}");
                }

                if (string.IsNullOrEmpty(title))
                    title = "Stream YouTube en direct";
                if (string.IsNullOrEmpty(channelName))
                    channelName = url.Split('/').Last();

                // --- Statut final : Priorité à l'API YouTube, fallback sur Streamlink ---
                isLive = apiLiveFound || streamlinkIsLive;
                _logger.LogInformation($"[DIAG] streamlinkIsLive = {streamlinkIsLive}, isLive (API) = {isLive}, apiLiveFound = {apiLiveFound}");
                _logger.LogInformation($"StreamId final pour la concaténation : {streamId}");

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

        private async Task<StreamInfo> GetStreamInfoOptimizedAsync(string url, string streamlinkVideoId, string streamlinkAuthor, string streamlinkTitle)
        {
            // Priorité 1 : Utiliser les informations de Streamlink si disponibles
            if (!string.IsNullOrEmpty(streamlinkVideoId) && !string.IsNullOrEmpty(streamlinkAuthor))
            {
                _logger.LogInformation("Utilisation des informations Streamlink en priorité");
                
                // Essayer seulement l'API pour le titre si pas disponible via Streamlink
                string title = streamlinkTitle;
                if (string.IsNullOrEmpty(title))
                {
                    try
                    {
                        var apiUrl = $"https://www.googleapis.com/youtube/v3/videos?part=snippet&id={streamlinkVideoId}&key={YOUTUBE_API_KEY}";
                        var response = await CallYouTubeApiAsync(apiUrl, $"titre vidéo pour {streamlinkVideoId}");
                        var jsonDoc = JsonDocument.Parse(response);
                        var items = jsonDoc.RootElement.GetProperty("items");
                        if (items.GetArrayLength() > 0)
                        {
                            title = items[0].GetProperty("snippet").GetProperty("title").GetString();
                            _logger.LogInformation($"Titre récupéré via API : {title}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Impossible de récupérer le titre via API (ignoré) : {ex.Message}");
                    }
                }
                
                return new StreamInfo
                {
                    Url = url,
                    StreamerName = streamlinkAuthor,
                    StreamTitle = title ?? streamlinkTitle ?? "Stream en direct",
                    Platform = "YouTube",
                    ChannelName = streamlinkAuthor,
                    StreamUrl = await GetStreamUrlAsync(url),
                    Title = title ?? streamlinkTitle ?? "Stream en direct",
                    IsLive = true,
                    ChannelFolderName = streamlinkAuthor,
                    StreamId = streamlinkVideoId
                };
            }
            
            // Priorité 2 : Fallback sur l'API YouTube complète
            return await GetStreamInfoFullApiAsync(url, streamlinkVideoId, streamlinkAuthor, streamlinkTitle);
        }
        
        private async Task<StreamInfo> GetStreamInfoFullApiAsync(string url, string streamlinkVideoId, string streamlinkAuthor, string streamlinkTitle)
        {
            // Méthode existante avec tous les appels API
            // ... (garder la logique existante)
            return null; // Placeholder - la logique existante reste
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

        private async Task<string> CallYouTubeApiAsync(string apiUrl, string operationName, int maxRetries = 2)
        {
            // Vérifie si la réponse est déjà en cache
            if (_apiCache.TryGetValue(apiUrl, out var cachedResponse))
            {
                _logger.LogInformation($"[CACHE] Utilisation de la réponse en cache pour {operationName}");
                return cachedResponse;
            }
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    _logger.LogInformation($"[API] Tentative {attempt}/{maxRetries} pour {operationName}");
                    var response = await _httpClient.GetStringAsync(apiUrl);
                    _logger.LogInformation($"[API] {operationName} réussie");
                    // Stocke la réponse dans le cache
                    _apiCache[apiUrl] = response;
                    return response;
                }
                catch (HttpRequestException ex) when (ex.Message.Contains("403"))
                {
                    _logger.LogWarning($"[API] Erreur 403 (quota dépassé) pour {operationName} - tentative {attempt}/{maxRetries}");
                    if (attempt < maxRetries)
                    {
                        var delay = attempt * 1000; // Délai progressif : 1s, 2s
                        _logger.LogInformation($"[API] Attente de {delay}ms avant nouvelle tentative...");
                        await Task.Delay(delay);
                    }
                    else
                    {
                        _logger.LogError($"[API] Échec définitif pour {operationName} après {maxRetries} tentatives");
                        throw;
                    }
                }
                catch (HttpRequestException ex) when (ex.Message.Contains("429"))
                {
                    _logger.LogWarning($"[API] Erreur 429 (rate limit) pour {operationName} - tentative {attempt}/{maxRetries}");
                    if (attempt < maxRetries)
                    {
                        var delay = attempt * 2000; // Délai plus long pour rate limit : 2s, 4s
                        _logger.LogInformation($"[API] Attente de {delay}ms avant nouvelle tentative...");
                        await Task.Delay(delay);
                    }
                    else
                    {
                        _logger.LogError($"[API] Échec définitif pour {operationName} après {maxRetries} tentatives");
                        throw;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[API] Erreur inattendue pour {operationName} : {ex.Message}");
                    throw;
                }
            }
            return null;
        }
    }
} 