using System.Threading.Tasks;
using AutoStreamRec.Models;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace AutoStreamRec.Services
{
    public class TwitchStreamDetector : BaseStreamDetector
    {
        public TwitchStreamDetector(Action<string> logAction, ExecutableLocator locator)
            : base(logAction, locator)
        {
        }

        public override bool CanHandle(string url)
        {
            return url.Contains("twitch.tv");
        }

        protected override string GetPlatformName()
        {
            return "Twitch";
        }

        public override async Task<StreamInfo> DetectStream(string url)
        {
            var streamInfo = new StreamInfo
            {
                IsLive = false,
                Platform = GetPlatformName(),
                DetectionTime = DateTime.Now,
                Url = url
            };

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "streamlink",
                    Arguments = $"--json \"{url}\" best",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                string jsonContent = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                // Sauvegarder le JSON dans un fichier pour debug
                try
                {
                    var logsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "recordings", "logs");
                    Directory.CreateDirectory(logsDir);
                    File.WriteAllText(Path.Combine(logsDir, "twitch_last.json"), jsonContent);
                }
                catch { }

                if (!string.IsNullOrWhiteSpace(jsonContent))
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(jsonContent);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("error", out var errorProp))
                    {
                        _log($"Erreur Streamlink: {errorProp.GetString()}");
                    }
                    else if (
                        (root.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "hls") &&
                        (root.TryGetProperty("url", out var urlProp) || root.TryGetProperty("master", out var masterProp))
                    )
                    {
                        streamInfo.IsLive = true;
                        // Enrichir les infos
                        if (root.TryGetProperty("metadata", out var meta))
                        {
                            if (meta.TryGetProperty("author", out var author))
                                streamInfo.StreamerName = author.GetString();
                            if (meta.TryGetProperty("title", out var title))
                                streamInfo.Title = title.GetString();
                        }
                    }
                    else
                    {
                        _log($"Aucun live détecté pour cette chaîne Twitch.");
                    }
                }
                else
                {
                    _log($"Réponse vide de Streamlink lors de la détection du live Twitch.");
                }
            }
            catch (Exception ex)
            {
                _log($"Erreur lors de la détection du live Twitch: {ex.Message}");
            }

            // Extraire le nom de la chaîne
            if (url.Contains("twitch.tv/"))
            {
                int startIndex = url.IndexOf("twitch.tv/") + 10;
                int endIndex = url.IndexOf('/', startIndex);
                streamInfo.ChannelName = endIndex == -1 
                    ? url.Substring(startIndex)
                    : url.Substring(startIndex, endIndex - startIndex);
            }
            else
            {
                streamInfo.ChannelName = "Unknown_Channel";
            }

            return streamInfo;
        }
    }
} 