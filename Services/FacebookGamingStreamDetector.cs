using System.Threading.Tasks;
using AutoStreamRec.Models;

namespace AutoStreamRec.Services
{
    public class FacebookGamingStreamDetector : BaseStreamDetector
    {
        public FacebookGamingStreamDetector(Action<string> logAction, ExecutableLocator locator)
            : base(logAction, locator)
        {
        }

        public override bool CanHandle(string url)
        {
            return url.Contains("facebook.com") || url.Contains("fb.gg");
        }

        protected override string GetPlatformName()
        {
            return "Facebook Gaming";
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
            if (url.Contains("facebook.com/"))
            {
                int startIndex = url.IndexOf("facebook.com/") + 13;
                int endIndex = url.IndexOf('/', startIndex);
                streamInfo.ChannelName = endIndex == -1 
                    ? url.Substring(startIndex)
                    : url.Substring(startIndex, endIndex - startIndex);
            }
            else if (url.Contains("fb.gg/"))
            {
                int startIndex = url.IndexOf("fb.gg/") + 6;
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