using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Strivea.Models;
using Strivea.Helpers;

namespace Strivea.Services
{
    public class VideoConverter
    {
        private readonly ExecutableLocator _executableLocator;
        private readonly ILogger<VideoConverter> _logger;

        public VideoConverter(ExecutableLocator executableLocator, ILogger<VideoConverter> logger)
        {
            _executableLocator = executableLocator;
            _logger = logger;
        }

        public async Task<bool> ConvertVideoAsync(string inputPath, string outputPath)
        {
            try
            {
                var ffmpegPath = _executableLocator.FindExecutable("ffmpeg");
                if (string.IsNullOrEmpty(ffmpegPath))
                {
                    _logger.LogError("FFmpeg non trouvé");
                    return false;
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = $"-i \"{inputPath}\" -c:v copy -c:a copy \"{outputPath}\"",
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
                    _logger.LogError($"Erreur lors de la conversion : {error}");
                    return false;
                }

                _logger.LogInformation($"Conversion réussie : {outputPath}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la conversion de la vidéo");
                return false;
            }
        }

        public async Task ConvertDirectory(string inputDirectory, string outputDirectory)
        {
            try
            {
                if (!Directory.Exists(inputDirectory))
                {
                    _logger.LogError($"Le répertoire d'entrée n'existe pas : {inputDirectory}");
                    return;
                }

                Directory.CreateDirectory(outputDirectory);

                var files = Directory.GetFiles(inputDirectory, "*.ts");
                foreach (var file in files)
                {
                    var fileName = Path.GetFileNameWithoutExtension(file);
                    var outputPath = Path.Combine(outputDirectory, $"{fileName}.mp4");
                    await ConvertVideoAsync(file, outputPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur lors de la conversion du répertoire : {ex.Message}");
            }
        }

        public async Task<bool> ConvertToMp4(string inputPath)
        {
            try
            {
                var ffmpegPath = _executableLocator.FindExecutable("ffmpeg");
                if (string.IsNullOrEmpty(ffmpegPath))
                {
                    _logger.LogError("FFmpeg non trouvé");
                    return false;
                }

                var outputPath = Path.ChangeExtension(inputPath, ".mp4");
                var startInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = $"-i \"{inputPath}\" -c:v copy -c:a copy \"{outputPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = startInfo };
                process.Start();

                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();

                if (!string.IsNullOrEmpty(error))
                {
                    _logger.LogError($"Erreur lors de la conversion : {error}");
                    return false;
                }

                _logger.LogInformation($"Conversion réussie : {outputPath}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la conversion");
                return false;
            }
        }
    }
}
