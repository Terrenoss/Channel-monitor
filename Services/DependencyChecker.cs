using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AutoStreamRec.Services
{
    public class DependencyChecker
    {
        private readonly Action<string> _log;
        private readonly ExecutableLocator _locator;

        public DependencyChecker(Action<string> logAction, ExecutableLocator locator)
        {
            _log = logAction;
            _locator = locator;
        }

        public Task<bool> CheckDependencies()
        {
            _log("Vérification des dépendances...");

            var dependencies = new Dictionary<string, string>
            {
                { "streamlink", "Streamlink (pip install streamlink)" },
                { "ffmpeg", "FFmpeg (https://ffmpeg.org/)" }
            };

            bool allOk = true;

            foreach (var dep in dependencies)
            {
                string path = _locator.FindExecutablePath(dep.Key);
                if (string.IsNullOrEmpty(path))
                {
                    _log($"ERREUR: {dep.Value} non trouvé");
                    allOk = false;
                }
                else
                {
                    _log($"OK: {dep.Key} trouvé à {path}");
                }
            }

            return Task.FromResult(allOk);
        }
    }
}