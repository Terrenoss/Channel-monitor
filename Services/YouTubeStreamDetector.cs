using System.Threading.Tasks;
using AutoStreamRec.Models;

namespace AutoStreamRec.Services
{
    public class YouTubeStreamDetector : BaseStreamDetector
    {
        public YouTubeStreamDetector(Action<string> logAction, ExecutableLocator locator)
            : base(logAction, locator)
        {
        }

        public override bool CanHandle(string url)
        {
            return url.Contains("youtube.com") || url.Contains("youtu.be");
        }

        protected override string GetPlatformName()
        {
            return "YouTube";
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
            
            if (streamInfo.IsLive)
            {
                // Extraire le nom de la chaîne
                if (url.Contains("@"))
                {
                    int atIndex = url.IndexOf('@');
                    int nextSlash = url.IndexOf('/', atIndex);
                    streamInfo.ChannelName = nextSlash == -1 
                        ? url.Substring(atIndex + 1)
                        : url.Substring(atIndex + 1, nextSlash - atIndex - 1);
                }
                else
                {
                    streamInfo.ChannelName = "Unknown_Channel";
                }
            }

            return streamInfo;
        }
    }
} 