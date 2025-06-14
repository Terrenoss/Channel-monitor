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
            try
            {
                _logger.LogInformation($"Extraction de l'ID de la chaîne depuis l'URL : {url}");
                
                if (url.Contains("/channel/"))
                {
                    var channelId = url.Split("/channel/")[1].Split('/')[0];
                    _logger.LogInformation($"ID de chaîne trouvé (format /channel/) : {channelId}");
                    return channelId;
                }
                else if (url.Contains("/@"))
                {
                    var handle = url.Split("/@")[1].Split('/')[0];
                    _logger.LogInformation($"Handle trouvé : @{handle}");
                    
                    // Convertir le handle en ID de chaîne via l'API
                    var apiUrl = $"https://www.googleapis.com/youtube/v3/search?part=snippet&q=@{handle}&type=channel&key={YOUTUBE_API_KEY}";
                    _logger.LogInformation($"Requête API pour obtenir l'ID de chaîne : {apiUrl}");
                    
                    var response = _httpClient.GetStringAsync(apiUrl).GetAwaiter().GetResult();
                    var jsonDoc = JsonDocument.Parse(response);
                    
                    if (jsonDoc.RootElement.GetProperty("items").GetArrayLength() > 0)
                    {
                        var channelId = jsonDoc.RootElement
                            .GetProperty("items")[0]
                            .GetProperty("id")
                            .GetProperty("channelId")
                            .GetString();
                        _logger.LogInformation($"ID de chaîne trouvé pour @{handle} : {channelId}");
                        return channelId;
                    }
                }
                
                _logger.LogWarning("Impossible d'extraire l'ID de la chaîne");
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
            try
            {
                _logger.LogInformation($"Début de la récupération des informations du stream YouTube pour : {url}");

                var streamlinkPath = _executableLocator.FindExecutable("streamlink");
                if (string.IsNullOrEmpty(streamlinkPath))
                {
                    _logger.LogError("Streamlink non trouvé");
                    return new StreamInfo
                    {
                        ChannelName = url.Split('/')[^1],
                        StreamUrl = url,
                        Title = "Erreur : Streamlink non trouvé",
                        IsLive = false,
                        Platform = "YouTube"
                    };
                }

                _logger.LogInformation($"Streamlink trouvé à : {streamlinkPath}");

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

                if (process.ExitCode != 0 || string.IsNullOrEmpty(output))
                {
                    _logger.LogError($"Streamlink a échoué ou n'a pas produit de sortie pour {url}. Erreur: {error}");
                    return new StreamInfo
                    {
                        ChannelName = url.Split('/')[^1],
                        StreamUrl = url,
                        Title = "Stream YouTube en direct (non détecté)",
                        IsLive = false,
                        Platform = "YouTube"
                    };
                }

                // Parse the JSON output
                try
                {
                    using JsonDocument doc = JsonDocument.Parse(output);
                    JsonElement root = doc.RootElement;

                    bool isLive = false;
                    string title = "Stream YouTube en direct";
                    string channelName = url.Split('/')[^1]; // Default to last part of URL

                    _logger.LogInformation("Analyse de la réponse JSON de Streamlink...");

                    // Vérifier si c'est un stream HLS et en direct
                    if (root.TryGetProperty("type", out JsonElement typeElement))
                    {
                        var type = typeElement.GetString();
                        _logger.LogInformation($"Type de stream détecté : {type}");
                        
                        if (type == "hls")
                        {
                            if (root.TryGetProperty("url", out JsonElement urlElement))
                            {
                                var streamUrl = urlElement.GetString();
                                _logger.LogInformation($"URL du stream : {streamUrl}");
                                
                                // Vérifier si c'est un stream en direct en cherchant plusieurs indicateurs
                                if (streamUrl.Contains("live=1") || 
                                    streamUrl.Contains("playlist_type/LIVE") ||
                                    streamUrl.Contains("yt_live_broadcast"))
                                {
                                    isLive = true;
                                    _logger.LogInformation("Stream détecté comme étant en direct (indicateurs de live trouvés)");
                                }
                                else
                                {
                                    _logger.LogInformation("Stream non détecté comme étant en direct (aucun indicateur de live trouvé)");
                                }
                            }
                        }
                    }

                    if (root.TryGetProperty("metadata", out JsonElement metadataElement))
                    {
                        _logger.LogInformation("Métadonnées trouvées dans la réponse");
                        
                        if (metadataElement.TryGetProperty("title", out JsonElement titleElement))
                        {
                            title = titleElement.GetString();
                            _logger.LogInformation($"Titre du stream détecté : {title}");
                        }
                        
                        if (metadataElement.TryGetProperty("author", out JsonElement authorElement))
                        {
                            channelName = authorElement.GetString();
                            _logger.LogInformation($"Nom de la chaîne détecté : {channelName}");
                        }
                    }
                    else
                    {
                        _logger.LogInformation("Aucune métadonnée trouvée dans la réponse");
                    }

                    _logger.LogInformation($"État final de la détection - IsLive: {isLive}, Title: {title}, Channel: {channelName}");

                    return new StreamInfo
                    {
                        ChannelName = channelName,
                        StreamUrl = url,
                        Title = title,
                        IsLive = isLive,
                        Platform = "YouTube"
                    };
                }
                catch (JsonException jsonEx)
                {
                    _logger.LogError(jsonEx, $"Erreur lors de l'analyse de la sortie JSON de Streamlink pour {url}. Sortie : {output}");
                    return new StreamInfo
                    {
                        ChannelName = url.Split('/')[^1],
                        StreamUrl = url,
                        Title = "Erreur d'analyse JSON",
                        IsLive = false,
                        Platform = "YouTube"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erreur inattendue lors de la récupération des informations du stream pour {url}");
                return new StreamInfo
                {
                    ChannelName = url.Split('/')[^1],
                    StreamUrl = url,
                    Title = "Erreur de récupération d'informations",
                    IsLive = false,
                    Platform = "YouTube"
                };
            }
        }
    }
} 