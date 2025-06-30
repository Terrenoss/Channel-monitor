using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Linq;
using Strivea.Models;

namespace Strivea.Services
{
    public class TwitchStreamDetector : BaseStreamDetector
    {
        private readonly TwitchApiService _twitchApiService;

        public TwitchStreamDetector(ILogger logger, IExecutableLocator executableLocator, TwitchApiService twitchApiService)
            : base(logger, executableLocator)
        {
            _twitchApiService = twitchApiService;
        }

        public override bool CanHandle(string url)
        {
            return !string.IsNullOrEmpty(url) && url.Contains("twitch.tv");
        }

        public override string GetPlatformName()
        {
            return "Twitch";
        }

        public override async Task<StreamInfo> GetStreamInfoAsync(string url)
        {
            try
            {
                _logger.LogInformation($"Récupération des informations du stream Twitch pour : {url}");

                // Extraire le nom de la chaîne depuis l'URL
                var channelName = ExtractChannelName(url);
                if (string.IsNullOrEmpty(channelName))
                {
                    _logger.LogError("Impossible d'extraire le nom de la chaîne Twitch depuis l'URL");
                    return new StreamInfo
                    {
                        ChannelName = url.Split('/').Last(),
                        StreamUrl = url,
                        Title = "Erreur : nom de chaîne introuvable",
                        IsLive = false
                    };
                }

                // Appel à l'API Twitch
                var twitchInfo = await _twitchApiService.GetStreamInfoAsync(channelName);
                if (twitchInfo == null)
                {
                    return new StreamInfo
                    {
                        ChannelName = channelName,
                        StreamUrl = url,
                        Title = "Stream Twitch hors ligne",
                        IsLive = false,
                        Platform = "Twitch"
                    };
                }

                // Remplir StreamInfo enrichi
                return new StreamInfo
                {
                    ChannelName = twitchInfo.UserName,
                    StreamUrl = url,
                    Title = twitchInfo.Title,
                    StreamTitle = twitchInfo.Title,
                    IsLive = true,
                    Platform = "Twitch",
                    ChannelFolderName = twitchInfo.UserName,
                    Quality = null, // Peut être enrichi plus tard
                    ViewerCount = twitchInfo.ViewerCount,
                    DetectionTime = DateTime.Now,
                    ErrorMessage = null,
                    // Ajout d'un champ custom pour l'ID du stream (à ajouter dans StreamInfo si besoin)
                    // StreamId = twitchInfo.StreamId,
                    // GameName = twitchInfo.GameName,
                    // Language = twitchInfo.Language,
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erreur lors de la récupération des informations du stream Twitch pour {url}");
                return new StreamInfo
                {
                    ChannelName = url.Split('/').Last(),
                    StreamUrl = url,
                    Title = $"Erreur : {ex.Message}",
                    IsLive = false,
                    Platform = "Twitch"
                };
            }
        }

        private string ExtractChannelName(string url)
        {
            // Exemples d'URL : https://www.twitch.tv/pokimane
            var parts = url.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var idx = parts.ToList().FindIndex(p => p.Contains("twitch.tv"));
            if (idx >= 0 && idx + 1 < parts.Length)
                return parts[idx + 1];
            // Fallback : dernier segment
            return parts.LastOrDefault();
        }
    }
} 