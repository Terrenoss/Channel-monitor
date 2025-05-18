using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AutoStreamRec.Services
{
    public class StreamRecorderService
    {
        private const int CheckInterval = 30;
        private readonly Action<string> _log;
        private readonly Func<string, Task<bool>> _installDependencyHandler;

        public StreamRecorderService(Action<string> logAction, Func<string, Task<bool>> installDependencyHandler = null)
        {
            _log = logAction;
            _installDependencyHandler = installDependencyHandler;
        }

        public async Task<bool> CheckDependencies()
        {
            bool allOk = true;
            
            if (!await CheckFfmpegExists())
            {
                _log("FFmpeg n'est pas détecté");
                allOk = false;
            }

            if (!await CheckToolExists("streamlink", "pip install streamlink"))
            {
                _log("Streamlink n'est pas détecté");
                allOk = false;
            }

            if (!await CheckToolExists("yt-dlp", "pip install yt-dlp"))
            {
                _log("yt-dlp n'est pas détecté");
                allOk = false;
            }

            return allOk;
        }

        private async Task<bool> CheckFfmpegExists()
        {
            try
            {
                Process process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "ffmpeg",
                        Arguments = "-version",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        StandardOutputEncoding = Encoding.UTF8
                    }
                };

                StringBuilder output = new StringBuilder();
                process.OutputDataReceived += (sender, e) => 
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        output.AppendLine(e.Data);
                };

                process.Start();
                process.BeginOutputReadLine();
                await process.WaitForExitAsync();

                return process.ExitCode == 0 && output.ToString().Contains("ffmpeg version");
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> CheckToolExists(string toolName, string installCommand)
        {
            try
            {
                using (CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                {
                    Process process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = toolName,
                            Arguments = "--version",
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            StandardOutputEncoding = Encoding.UTF8
                        }
                    };

                    StringBuilder output = new StringBuilder();
                    process.OutputDataReceived += (sender, e) => 
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                            output.AppendLine(e.Data);
                    };
                    
                    process.Start();
                    process.BeginOutputReadLine();
                    await process.WaitForExitAsync(cts.Token);

                    return process.ExitCode == 0;
                }
            }
            catch
            {
                if (_installDependencyHandler != null)
                {
                    return await _installDependencyHandler($"Voulez-vous installer {toolName}?\n\nCommande: {installCommand}");
                }
                return false;
            }
        }

        public async Task RecordYouTubeStream(string youtubeUrl)
        {
            try
            {
                if (!await CheckDependencies())
                {
                    throw new Exception("Dépendances manquantes");
                }

                _log("Vérification de la chaîne YouTube...");
                
                // Trouver le stream actif
                string streamUrl = await FindActiveStream(youtubeUrl);
                if (string.IsNullOrEmpty(streamUrl))
                {
                    throw new Exception("Aucun stream actif trouvé sur cette chaîne");
                }

                _log($"Stream trouvé: {streamUrl}");

                string recordingsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "recordings");
                string tempDir = Path.Combine(recordingsDir, "temp");
                string outputDir = Path.Combine(recordingsDir, "YouTube");
                
                Directory.CreateDirectory(tempDir);
                Directory.CreateDirectory(outputDir);

                string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                string tempFile = Path.Combine(tempDir, $"{timestamp}.ts");
                string finalFile = Path.Combine(outputDir, $"{timestamp}.mp4");

                _log($"Début de l'enregistrement...");

                using (Process process = new Process())
                {
                    process.StartInfo = new ProcessStartInfo
                    {
                        FileName = "streamlink",
                        Arguments = $"{streamUrl} best --hls-live-restart -o \"{tempFile}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        StandardOutputEncoding = Encoding.UTF8
                    };

                    process.OutputDataReceived += (sender, e) => 
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                            _log(e.Data);
                    };
                    process.ErrorDataReceived += (sender, e) => 
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                            _log($"ERREUR: {e.Data}");
                    };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    await process.WaitForExitAsync();

                    if (File.Exists(tempFile) && new FileInfo(tempFile).Length > 0)
                    {
                        _log("Conversion en MP4...");
                        await ConvertToMp4(tempFile, finalFile);
                        _log($"Enregistrement terminé: {Path.GetFileName(finalFile)}");
                    }
                    else
                    {
                        _log("Aucune donnée enregistrée");
                    }
                }
            }
            catch (Exception ex)
            {
                _log($"ERREUR: {ex.Message}");
                throw;
            }
        }

        private async Task<string> FindActiveStream(string channelUrl)
        {
            try
            {
                using (var process = new Process())
                {
                    var outputBuilder = new StringBuilder();
                    
                    process.StartInfo = new ProcessStartInfo
                    {
                        FileName = "yt-dlp",
                        Arguments = $"{channelUrl} --skip-download --get-url --live-from-start",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        StandardOutputEncoding = Encoding.UTF8
                    };

                    process.OutputDataReceived += (sender, e) => 
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                            outputBuilder.AppendLine(e.Data);
                    };

                    process.Start();
                    process.BeginOutputReadLine();
                    await process.WaitForExitAsync();

                    var output = outputBuilder.ToString().Trim();
                    return string.IsNullOrEmpty(output) ? null : output;
                }
            }
            catch (Exception ex)
            {
                _log($"Erreur lors de la recherche du stream: {ex.Message}");
                return null;
            }
        }

        public async Task ConvertToMp4(string inputFile, string outputFile)
        {
            try
            {
                using (var process = new Process())
                {
                    process.StartInfo = new ProcessStartInfo
                    {
                        FileName = "ffmpeg",
                        Arguments = $"-i \"{inputFile}\" -c copy \"{outputFile}\" -loglevel warning",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        StandardOutputEncoding = Encoding.UTF8
                    };

                    process.ErrorDataReceived += (sender, e) => 
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                            _log($"FFmpeg: {e.Data}");
                    };

                    process.Start();
                    process.BeginErrorReadLine();
                    await process.WaitForExitAsync();

                    if (File.Exists(outputFile)) 
                    {
                        File.Delete(inputFile);
                    }
                }
            }
            catch (Exception ex)
            {
                _log($"ERREUR conversion: {ex.Message}");
                throw;
            }
        }
    }
}