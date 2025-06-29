using System;

namespace Strivea.Models;

public class StreamInfo
{
    public string Url { get; set; }
    public string StreamerName { get; set; }
    public string StreamTitle { get; set; }
    public string Platform { get; set; }
    public bool IsRecording { get; set; }
    public string ChannelName { get; set; }
    public string StreamUrl { get; set; }
    public string Title { get; set; }
    public bool IsLive { get; set; }
    public string ErrorMessage { get; set; }
    public DateTime DetectionTime { get; set; }
    public int ViewerCount { get; set; }
    public string Quality { get; set; }
    public string ChannelFolderName { get; set; }
    public string StreamId { get; set; }
}
