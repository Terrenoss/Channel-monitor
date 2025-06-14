using System;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Strivea.Models;
using System.IO;

namespace Strivea.Services
{
    public abstract class BaseStreamDetector : IStreamDetector
    {
        protected readonly ILogger _logger;
        protected readonly IExecutableLocator _executableLocator;

        protected BaseStreamDetector(ILogger logger, IExecutableLocator executableLocator)
        {
            _logger = logger;
            _executableLocator = executableLocator;
        }

        public abstract bool CanHandle(string url);
        public abstract Task<StreamInfo> GetStreamInfoAsync(string url);

        public virtual async Task<bool> IsLiveAsync(string url)
        {
            try
            {
                _logger.LogInformation($"Vérification du statut en direct pour {url}");
                var info = await GetStreamInfoAsync(url);
                return info.IsLive;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erreur lors de la vérification du statut en direct pour {url}");
                return false;
            }
        }

        public virtual async Task<string> GetStreamUrlAsync(string url)
        {
            try
            {
                _logger.LogInformation($"Récupération de l'URL du stream pour {url}");
                var streamlinkPath = _executableLocator.FindExecutable("streamlink");
                if (string.IsNullOrEmpty(streamlinkPath))
                {
                    _logger.LogError("Streamlink non trouvé");
                    return url;
                }

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = streamlinkPath,
                        Arguments = $"--stream-url {url} best",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (!string.IsNullOrEmpty(output))
                {
                    return output.Trim();
                }

                return url;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erreur lors de la récupération de l'URL du stream pour {url}");
                return url;
            }
        }

        public virtual string GetPlatformName()
        {
            return GetType().Name.Replace("StreamDetector", "");
        }

        protected StreamInfo CreateErrorInfo(string errorMessage)
        {
            return new StreamInfo
            {
                IsLive = false,
                ErrorMessage = errorMessage,
                DetectionTime = DateTime.Now
            };
        }
    }
} 