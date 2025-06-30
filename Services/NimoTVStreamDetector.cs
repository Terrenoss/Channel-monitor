using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Linq;
using Strivea.Models;

namespace Strivea.Services
{
    public class NimoTVStreamDetector : BaseStreamDetector
    {
        public NimoTVStreamDetector(ILogger logger, IExecutableLocator executableLocator)
            : base(logger, executableLocator)
        {
        }

        public override bool CanHandle(string url)
        {
            return !string.IsNullOrEmpty(url) && url.Contains("nimo.tv");
        }

        public override async Task<StreamInfo> GetStreamInfoAsync(string url)
        {
            try
            {
                _logger.LogInformation($"Récupération des informations du stream NimoTV pour : {url}");

                var streamlinkPath = _executableLocator.FindExecutable("streamlink");
                if (string.IsNullOrEmpty(streamlinkPath))
                {
                    _logger.LogError("Streamlink non trouvé");
                    return new StreamInfo
                    {
                        ChannelName = url.Split('/').Last(),
                        StreamUrl = url,
                        Title = "Erreur : Streamlink non trouvé",
                        IsLive = false
                    };
                }

                // TODO: Implémenter la détection réelle avec streamlink
                // Pour l'instant, on simule une détection
                await Task.Delay(1000);

                return new StreamInfo
                {
                    ChannelName = url.Split('/').Last(),
                    StreamUrl = url,
                    Title = "Stream NimoTV en direct",
                    IsLive = true
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erreur lors de la récupération des informations du stream NimoTV pour {url}");
                return new StreamInfo
                {
                    ChannelName = url.Split('/').Last(),
                    StreamUrl = url,
                    Title = $"Erreur : {ex.Message}",
                    IsLive = false
                };
            }
        }
    }
} 