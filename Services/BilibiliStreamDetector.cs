using System.Threading.Tasks;
using AutoStreamRec.Models;

namespace AutoStreamRec.Services
{
    public class BilibiliStreamDetector : BaseStreamDetector
    {
        public BilibiliStreamDetector(Action<string> logAction, ExecutableLocator locator)
            : base(logAction, locator)
        {
        }

        public override bool CanHandle(string url)
        {
            return url.Contains("bilibili.com");
        }

        protected override string GetPlatformName()
        {
            return "Bilibili";
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
            if (url.Contains("bilibili.com/"))
            {
                int startIndex = url.IndexOf("bilibili.com/") + 13;
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