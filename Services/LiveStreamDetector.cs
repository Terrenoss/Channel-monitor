using System;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AutoStreamRec.Services
{
    public class LiveStreamDetector
    {
        private readonly Action<string> _log;

        public LiveStreamDetector(Action<string> logAction)
        {
            _log = logAction;
        }

        public async Task<string> GetLiveStreamUrl(string channelUrl)
        {
            _log($"Détection de live pour: {channelUrl}");

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "streamlink",
                    Arguments = $"--json \"{channelUrl}\" best",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };

                using var process = new Process { StartInfo = startInfo };
                process.Start();
                string jsonContent = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                _log($"Réponse JSON brute: {jsonContent}");

                using var doc = JsonDocument.Parse(jsonContent);

                if (doc.RootElement.TryGetProperty("type", out var typeProp) &&
                    typeProp.GetString() == "hls")
                {
                    _log("Stream HLS valide détecté");
                    return "best";
                }

                if (doc.RootElement.TryGetProperty("streams", out var streams))
                {
                    foreach (var stream in streams.EnumerateObject())
                        return stream.Name;
                }
            }
            catch (JsonException ex)
            {
                _log($"ERREUR Parsing JSON: {ex.Message}");
            }
            catch (Exception ex)
            {
                _log($"ERREUR Détection: {ex.Message}");
            }

            return null;
        }
    }
}
