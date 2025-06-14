using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace Strivea.Services
{
    public class DependencyChecker
    {
        private readonly ExecutableLocator _executableLocator;
        private readonly ILogger<DependencyChecker> _logger;

        public DependencyChecker(ExecutableLocator executableLocator, ILogger<DependencyChecker> logger)
        {
            _executableLocator = executableLocator;
            _logger = logger;
        }

        public async Task<Dictionary<string, bool>> CheckDependenciesAsync()
        {
            var results = new Dictionary<string, bool>();

            try
            {
                // Vérifier streamlink
                var streamlinkPath = _executableLocator.FindExecutable("streamlink");
                results["streamlink"] = !string.IsNullOrEmpty(streamlinkPath);
                _logger.LogInformation($"Streamlink trouvé : {streamlinkPath}");

                // Vérifier ffmpeg
                var ffmpegPath = _executableLocator.FindExecutable("ffmpeg");
                results["ffmpeg"] = !string.IsNullOrEmpty(ffmpegPath);
                _logger.LogInformation($"FFmpeg trouvé : {ffmpegPath}");

                // Vérifier Python
                var pythonPath = _executableLocator.FindExecutable("python");
                results["python"] = !string.IsNullOrEmpty(pythonPath);
                _logger.LogInformation($"Python trouvé : {pythonPath}");

                // Vérifier pip
                var pipPath = _executableLocator.FindExecutable("pip");
                results["pip"] = !string.IsNullOrEmpty(pipPath);
                _logger.LogInformation($"Pip trouvé : {pipPath}");

                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la vérification des dépendances");
                throw;
            }
        }

        public bool AreAllDependenciesInstalled()
        {
            var results = CheckDependenciesAsync().GetAwaiter().GetResult();
            return results.Values.All(x => x);
        }

        public async Task InstallDependencies()
        {
            try
            {
                // Installer Streamlink via pip
                var pipPath = _executableLocator.FindExecutable("pip");
                if (string.IsNullOrEmpty(pipPath))
                {
                    throw new InvalidOperationException("pip n'est pas installé.");
                }

                _logger.LogInformation("Installation de Streamlink...");
                var startInfo = new ProcessStartInfo
                {
                    FileName = pipPath,
                    Arguments = "install streamlink",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    throw new Exception($"L'installation de Streamlink a échoué avec le code de sortie {process.ExitCode}");
                }

                _logger.LogInformation("Streamlink installé avec succès.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'installation des dépendances");
                throw;
            }
        }
    }
}