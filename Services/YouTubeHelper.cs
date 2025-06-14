using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Strivea.Services
{
    public class YouTubeHelper
    {
        private readonly ILogger<YouTubeHelper> _logger;
        private readonly ExecutableLocator _executableLocator;

        public YouTubeHelper(ILogger<YouTubeHelper> logger, ExecutableLocator executableLocator)
        {
            _logger = logger;
            _executableLocator = executableLocator;
        }

        public async Task<string> GetChannelNameAsync(string url)
        {
            try
            {
                var ytDlpPath = _executableLocator.FindExecutable("yt-dlp");
                if (string.IsNullOrEmpty(ytDlpPath))
                {
                    _logger.LogError("yt-dlp non trouvé");
                    return null;
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = ytDlpPath,
                    Arguments = $"--skip-download --print channel {url}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = startInfo };
                process.Start();

                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    _logger.LogError($"Erreur yt-dlp : {error}");
                    return null;
                }

                return output.Trim();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la récupération du nom de la chaîne");
                return null;
            }
        }

        public async Task<string> GetStreamTitleAsync(string url)
        {
            try
            {
                var ytDlpPath = _executableLocator.FindExecutable("yt-dlp");
                if (string.IsNullOrEmpty(ytDlpPath))
                {
                    _logger.LogError("yt-dlp non trouvé");
                    return null;
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = ytDlpPath,
                    Arguments = $"--skip-download --print title {url}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = startInfo };
                process.Start();

                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    _logger.LogError($"Erreur yt-dlp : {error}");
                    return null;
                }

                return output.Trim();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la récupération du titre du stream");
                return null;
            }
        }
    }
}