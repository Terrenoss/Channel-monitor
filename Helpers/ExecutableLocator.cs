using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Strivea.Helpers
{
    public class ExecutableLocator
    {
        private readonly ILogger<ExecutableLocator> _logger;

        public ExecutableLocator(ILogger<ExecutableLocator> logger)
        {
            _logger = logger;
        }

        public string FindExecutable(string executableName)
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var path = Environment.GetEnvironmentVariable("PATH");
                    if (string.IsNullOrEmpty(path))
                    {
                        _logger.LogError("La variable d'environnement PATH est vide");
                        return null;
                    }

                    foreach (var directory in path.Split(Path.PathSeparator))
                    {
                        var fullPath = Path.Combine(directory, executableName);
                        if (File.Exists(fullPath))
                        {
                            _logger.LogInformation($"Exécutable trouvé : {fullPath}");
                            return fullPath;
                        }
                    }
                }
                else
                {
                    var process = new System.Diagnostics.Process
                    {
                        StartInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "which",
                            Arguments = executableName,
                            RedirectStandardOutput = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };

                    process.Start();
                    var output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();

                    if (process.ExitCode == 0)
                    {
                        var path = output.Trim();
                        _logger.LogInformation($"Exécutable trouvé : {path}");
                        return path;
                    }
                }

                _logger.LogWarning($"Exécutable non trouvé : {executableName}");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erreur lors de la recherche de l'exécutable {executableName}");
                return null;
            }
        }

        public string FindExecutablePath(string executableName)
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var path = Environment.GetEnvironmentVariable("PATH");
                    if (string.IsNullOrEmpty(path))
                    {
                        _logger.LogError("La variable d'environnement PATH est vide");
                        return null;
                    }

                    foreach (var directory in path.Split(Path.PathSeparator))
                    {
                        var fullPath = Path.Combine(directory, executableName);
                        if (File.Exists(fullPath))
                        {
                            _logger.LogInformation($"Exécutable trouvé : {fullPath}");
                            return fullPath;
                        }
                    }
                }
                else
                {
                    var process = new System.Diagnostics.Process
                    {
                        StartInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "which",
                            Arguments = executableName,
                            RedirectStandardOutput = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };

                    process.Start();
                    var output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();

                    if (process.ExitCode == 0)
                    {
                        var path = output.Trim();
                        _logger.LogInformation($"Exécutable trouvé : {path}");
                        return path;
                    }
                }

                _logger.LogWarning($"Exécutable non trouvé : {executableName}");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erreur lors de la recherche de l'exécutable {executableName}");
                return null;
            }
        }
    }
} 