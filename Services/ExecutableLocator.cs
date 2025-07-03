using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Strivea.Services
{
    public class ExecutableLocator : IExecutableLocator, IDisposable
    {
        private readonly ILogger<ExecutableLocator> _logger;
        private readonly ConcurrentDictionary<string, string> _executableCache = new();
        private bool _disposed = false;

        public ExecutableLocator(ILogger<ExecutableLocator> logger)
        {
            _logger = logger;
        }

        public string FindExecutable(string executableName)
        {
            ThrowIfDisposed();
            
            try
            {
                // Vérifier le cache d'abord
                if (_executableCache.TryGetValue(executableName, out var cachedPath))
                {
                    // Vérifier que le fichier existe toujours
                    if (File.Exists(cachedPath))
                    {
                        return cachedPath;
                    }
                    else
                    {
                        // Supprimer du cache si le fichier n'existe plus
                        _executableCache.TryRemove(executableName, out _);
                    }
                }

                // Vérifier d'abord dans le répertoire courant
                var currentDir = AppDomain.CurrentDomain.BaseDirectory;
                var localPath = Path.Combine(currentDir, executableName);
                if (File.Exists(localPath))
                {
                    _executableCache[executableName] = localPath;
                    return localPath;
                }

                // Vérifier dans le PATH
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "where",
                        Arguments = executableName,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                if (!string.IsNullOrEmpty(output))
                {
                    var firstPath = output.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)[0];
                    if (File.Exists(firstPath))
                    {
                        _executableCache[executableName] = firstPath;
                        return firstPath;
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"Erreur lors de la recherche de l'exécutable {executableName}");
                return null;
            }
        }

        public bool IsExecutableInstalled(string executableName)
        {
            return !string.IsNullOrEmpty(FindExecutable(executableName));
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ExecutableLocator));
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                try
                {
                    _executableCache.Clear();
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Erreur lors du dispose de ExecutableLocator");
                }
                finally
                {
                    _disposed = true;
                }
            }
        }

        ~ExecutableLocator()
        {
            Dispose(false);
        }
    }
}
