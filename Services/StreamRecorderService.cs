using System;
using System.Threading;
using System.Threading.Tasks;

namespace AutoStreamRec.Services
{
    public class StreamRecorderService
    {
        private readonly YouTubeStreamRecorder _recorder;
        private readonly LiveStreamDetector _detector;
        private readonly DependencyChecker _checker;
        private readonly VideoConverter _converter;

        public StreamRecorderService(Action<string> logAction, Action<string> statAction)
        {
            var locator = new ExecutableLocator(logAction);
            _converter = new VideoConverter(logAction, locator);
            var statsLogger = new RecordingStatsLogger(statAction);
            var fileHelper = new FileHelper(logAction);
            var ytHelper = new YouTubeHelper(locator);

            _checker = new DependencyChecker(logAction, locator);
            _detector = new LiveStreamDetector(logAction);
            _recorder = new YouTubeStreamRecorder(logAction, fileHelper, ytHelper, statsLogger, _converter, locator);
        }

        public Task<bool> CheckDependencies() => _checker.CheckDependencies();

        public Task<string> GetLiveStreamUrl(string channelUrl) => _detector.GetLiveStreamUrl(channelUrl);

        public Task<bool> RecordYouTubeStream(string youtubeUrl, CancellationToken token) => _recorder.RecordStream(youtubeUrl, token);

        public void StopRecording() => _recorder.Stop();

        public Task<bool> ConvertToMp4(string tsFilePath, string mp4FilePath = null)
        {
            return _converter.ConvertToMp4(tsFilePath, mp4FilePath);
        }
    }
}