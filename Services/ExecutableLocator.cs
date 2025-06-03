using System;
using System.Diagnostics;
using System.IO;

namespace AutoStreamRec.Services
{
    public class ExecutableLocator
    {
        private readonly Action<string> _log;

        public ExecutableLocator(Action<string> logAction)
        {
            _log = logAction;
        }

        public string FindExecutablePath(string executable)
        {
            try
            {
                string envPath = Environment.GetEnvironmentVariable("PATH");
                if (!string.IsNullOrEmpty(envPath))
                {
                    foreach (var path in envPath.Split(Path.PathSeparator))
                    {
                        var fullPath = Path.Combine(path, executable + ".exe");
                        if (File.Exists(fullPath))
                            return fullPath;
                    }
                }

                var commonPaths = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Streamlink", "bin"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Streamlink", "bin"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python", "Python311", "Scripts"),
                    Path.Combine("C:", "ffmpeg", "bin"),
                    AppContext.BaseDirectory
                };

                foreach (var path in commonPaths)
                {
                    var fullPath = Path.Combine(path, executable + ".exe");
                    if (File.Exists(fullPath))
                        return fullPath;
                }

                try
                {
                    var proc = new Process()
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = executable,
                            Arguments = "--version",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true
                        }
                    };
                    if (proc.Start())
                    {
                        proc.Kill();
                        return executable;
                    }
                }
                catch { }
            }
            catch (Exception ex)
            {
                _log?.Invoke($"Erreur FindExecutablePath: {ex.Message}");
            }

            return null;
        }
    }
}
