using System;
using System.Threading;
using System.Threading.Tasks;

namespace AutoStreamRec.Services
{
    public class StreamRecorderService
    {
        private readonly YouTubeStreamRecorder _streamRecorder;
        private readonly ExecutableLocator _locator;
        private readonly FileHelper _fileHelper;
        private readonly YouTubeHelper _ytHelper;
        private readonly VideoConverter _converter;
        private readonly RecordingStatsLogger _stats;

        public StreamRecorderService(
            Action<string> logAction,
            Action<string> statsAction)
        {
            _locator = new ExecutableLocator(logAction);
            _fileHelper = new FileHelper(logAction);
            _ytHelper = new YouTubeHelper(_locator);
            _stats = new RecordingStatsLogger(statsAction);
            _converter = new VideoConverter(logAction, _locator);
            _streamRecorder = new YouTubeStreamRecorder(logAction, _fileHelper, _ytHelper, _stats, _converter, _locator);
        }

        public async Task<bool> CheckDependencies()
        {
            // ✅ à adapter si tu veux une vérification plus fine
            return !string.IsNullOrEmpty(_locator.FindExecutablePath("streamlink"))
                && !string.IsNullOrEmpty(_locator.FindExecutablePath("ffmpeg"))
                && !string.IsNullOrEmpty(_locator.FindExecutablePath("yt-dlp"));
        }

        public async Task<string> GetLiveStreamUrl(string url)
        {
            return await _ytHelper.GetChannelNameAsync(url); // tu peux ajuster si tu as une vraie détection
        }

        public async Task<bool> RecordYouTubeStream(string url, CancellationToken token)
        {
            return await _streamRecorder.RecordStream(url, token);
        }

        public void StopRecording()
        {
            _streamRecorder.Stop();
        }

        public string GetLastStreamQuality()
        {
            return _streamRecorder.LastDetectedQuality;
        }

        public async Task<bool> ConvertToMp4(string tsPath)
        {
            return await _converter.ConvertToMp4(tsPath);
        }
    }
}
