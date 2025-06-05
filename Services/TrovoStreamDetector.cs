using System.Threading.Tasks;
using AutoStreamRec.Models;

namespace AutoStreamRec.Services
{
    public class TrovoStreamDetector : BaseStreamDetector
    {
        public TrovoStreamDetector(Action<string> logAction, ExecutableLocator locator)
            : base(logAction, locator)
        {
        }

        public override bool CanHandle(string url)
        {
            return url.Contains("trovo.live");
        }

        protected override string GetPlatformName()
        {
            return "Trovo";
        }

        public override async Task<StreamInfo> DetectStream(string url)
        {
            var streamInfo = new StreamInfo
            {
                IsLive = true,
                Platform = GetPlatformName(),
                DetectionTime = DateTime.Now,
                Url = url
            };

            // Extraire le nom de la chaîne
            if (url.Contains("trovo.live/"))
            {
                int startIndex = url.IndexOf("trovo.live/") + 11;
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