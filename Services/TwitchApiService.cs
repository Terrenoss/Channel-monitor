using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Strivea.Services
{
    public class TwitchApiService
    {
        private readonly string _clientId;
        private readonly string _accessToken;
        private readonly HttpClient _httpClient;

        public TwitchApiService(string clientId, string accessToken)
        {
            _clientId = clientId;
            _accessToken = accessToken;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("Client-ID", _clientId);
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_accessToken}");
        }

        public async Task<TwitchStreamInfo> GetStreamInfoAsync(string channelName)
        {
            var url = $"https://api.twitch.tv/helix/streams?user_login={channelName}";
            var response = await _httpClient.GetStringAsync(url);
            var json = JsonDocument.Parse(response);

            if (json.RootElement.TryGetProperty("data", out var data) && data.GetArrayLength() > 0)
            {
                var stream = data[0];
                return new TwitchStreamInfo
                {
                    StreamId = stream.GetProperty("id").GetString(),
                    UserName = stream.GetProperty("user_name").GetString(),
                    Title = stream.GetProperty("title").GetString(),
                    GameName = stream.GetProperty("game_name").GetString(),
                    ViewerCount = stream.GetProperty("viewer_count").GetInt32(),
                    StartedAt = stream.GetProperty("started_at").GetDateTime(),
                    Language = stream.GetProperty("language").GetString()
                };
            }
            return null; // Pas de live en cours
        }
    }

    public class TwitchStreamInfo
    {
        public string StreamId { get; set; }
        public string UserName { get; set; }
        public string Title { get; set; }
        public string GameName { get; set; }
        public int ViewerCount { get; set; }
        public DateTime StartedAt { get; set; }
        public string Language { get; set; }
    }
} 