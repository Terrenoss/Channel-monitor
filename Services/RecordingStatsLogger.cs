using System;

namespace AutoStreamRec.Services
{
    public class RecordingStatsLogger
    {
        private readonly Action<string> _statAction;

        public RecordingStatsLogger(Action<string> statAction)
        {
            _statAction = statAction;
        }

        public void Log(DateTime startTime, long bytesRecorded)
        {
            var duration = DateTime.Now - startTime;
            double mbRecorded = bytesRecorded / (1024.0 * 1024.0);
            double mbPerMinute = duration.TotalMinutes > 0 ? mbRecorded / duration.TotalMinutes : 0;

            _statAction($"{duration:hh\\:mm\\:ss} | {mbRecorded:F2} MB | {mbPerMinute:F2} MB/min");
        }
    }
}