using System;

namespace Strivea.Models
{
    public class RecordingInfo
    {
        public string FilePath { get; set; }
        public TimeSpan Duration { get; set; }
        public long FileSize { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public string ChannelName { get; set; }
        public string Platform { get; set; }
    }
} 