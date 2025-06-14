using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Strivea.Models;

namespace Strivea.Services
{
    public class StreamDetectorFactory
    {
        private readonly ILogger _logger;
        private readonly IExecutableLocator _executableLocator;
        private readonly Dictionary<string, IStreamDetector> _detectors;

        public StreamDetectorFactory(ILogger logger, IExecutableLocator executableLocator)
        {
            _logger = logger;
            _executableLocator = executableLocator;
            _detectors = new Dictionary<string, IStreamDetector>
            {
                { "twitch", new TwitchStreamDetector(_logger, _executableLocator) },
                { "bilibili", new BilibiliStreamDetector(_logger, _executableLocator) },
                { "17live", new SeventeenLiveStreamDetector(_logger, _executableLocator) },
                { "tiktok", new TikTokLiveStreamDetector(_logger, _executableLocator) },
                { "vkplay", new VKPlayLiveStreamDetector(_logger, _executableLocator) },
                { "niconico", new NiconicoStreamDetector(_logger, _executableLocator) },
                { "facebook", new FacebookGamingStreamDetector(_logger, _executableLocator) },
                { "afreeca", new AfreecaTVStreamDetector(_logger, _executableLocator) },
                { "kick", new KickStreamDetector(_logger, _executableLocator) },
                { "bigo", new BigoLiveStreamDetector(_logger, _executableLocator) },
                { "nimo", new NimoTVStreamDetector(_logger, _executableLocator) },
                { "kuaishou", new KuaishouStreamDetector(_logger, _executableLocator) },
                { "dlive", new DLiveStreamDetector(_logger, _executableLocator) },
                { "trovo", new TrovoStreamDetector(_logger, _executableLocator) }
            };
        }

        public IStreamDetector GetDetector(string url)
        {
            return _detectors.Values.FirstOrDefault(detector => detector.CanHandle(url));
        }
    }
} 