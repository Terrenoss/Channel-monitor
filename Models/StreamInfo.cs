using System;

namespace AutoStreamRec.Models;

public class StreamInfo
{
    public bool IsLive { get; set; }
    public string StreamerName { get; set; }
    public string ChannelName { get; set; }
    public string Title { get; set; }
    public string Url { get; set; }
    public string Platform { get; set; }
    public DateTime DetectionTime { get; set; }
    public string ErrorMessage { get; set; }
    public int Viewers { get; set; }
    public string StreamCategory { get; set; }
    public string ThumbnailUrl { get; set; }
    public bool IsPartner { get; set; }
    public string Language { get; set; }
    public TimeSpan StreamDuration { get; set; }
    public string Quality { get; set; } = "best";
}
