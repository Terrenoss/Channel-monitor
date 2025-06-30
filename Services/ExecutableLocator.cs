using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;

namespace Strivea.Services
{
    public class ExecutableLocator : IExecutableLocator
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
                // Vérifier d'abord dans le répertoire courant
                var currentDir = AppDomain.CurrentDomain.BaseDirectory;
                var localPath = Path.Combine(currentDir, executableName);
                if (File.Exists(localPath))
                {
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
                    return output.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)[0];
                }

                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public bool IsExecutableInstalled(string executableName)
        {
            return !string.IsNullOrEmpty(FindExecutable(executableName));
        }
    }
}
