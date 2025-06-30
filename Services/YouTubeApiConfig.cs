namespace Strivea.Services
{
    public class YouTubeApiConfig
    {
        public string ApiKey { get; set; } = string.Empty;
        public int MaxRetries { get; set; } = 2;
        public int RetryDelayMs { get; set; } = 1000;
        public bool EnableOptimization { get; set; } = true;
    }
} 