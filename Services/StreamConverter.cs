using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Strivea.Services
{
    public class StreamConverter
    {
        private readonly ExecutableLocator _executableLocator;
        private readonly Action<string> _logAction;
        private Process _conversionProcess;
        private bool _isConverting;

        public StreamConverter(ExecutableLocator executableLocator, Action<string> logAction)
        {
            _executableLocator = executableLocator;
            _logAction = logAction;
        }

        public async Task ConvertToMp4(string inputFile, string outputDirectory)
        {
            if (_isConverting)
            {
                _logAction("Une conversion est déjà en cours.");
                return;
            }

            try
            {
                var ffmpegPath = _executableLocator.FindExecutable("ffmpeg");
                if (string.IsNullOrEmpty(ffmpegPath))
                {
                    throw new FileNotFoundException("FFmpeg n'a pas été trouvé.");
                }

                var inputFileName = Path.GetFileNameWithoutExtension(inputFile);
                var outputPath = Path.Combine(outputDirectory, $"{inputFileName}.mp4");

                var startInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = $"-i \"{inputFile}\" -c:v copy -c:a copy \"{outputPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                _conversionProcess = new Process { StartInfo = startInfo };
                _conversionProcess.OutputDataReceived += (sender, e) => 
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        _logAction($"FFmpeg: {e.Data}");
                };
                _conversionProcess.ErrorDataReceived += (sender, e) => 
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        _logAction($"FFmpeg Error: {e.Data}");
                };

                _conversionProcess.Start();
                _conversionProcess.BeginOutputReadLine();
                _conversionProcess.BeginErrorReadLine();

                _isConverting = true;
                _logAction($"Début de la conversion : {inputFile}");
                _logAction($"Fichier de sortie : {outputPath}");

                await _conversionProcess.WaitForExitAsync();

                if (_conversionProcess.ExitCode == 0)
                {
                    _logAction("Conversion terminée avec succès.");
                    File.Delete(inputFile);
                    _logAction($"Fichier source supprimé : {inputFile}");
                }
                else
                {
                    _logAction($"Erreur lors de la conversion. Code de sortie : {_conversionProcess.ExitCode}");
                }
            }
            catch (Exception ex)
            {
                _logAction($"Erreur lors de la conversion : {ex.Message}");
                throw;
            }
            finally
            {
                _isConverting = false;
                _conversionProcess = null;
            }
        }

        public void StopConversion()
        {
            if (!_isConverting || _conversionProcess == null)
            {
                return;
            }

            try
            {
                _conversionProcess.Kill();
                _conversionProcess.WaitForExit();
                _logAction("Conversion arrêtée.");
            }
            catch (Exception ex)
            {
                _logAction($"Erreur lors de l'arrêt de la conversion : {ex.Message}");
            }
            finally
            {
                _isConverting = false;
                _conversionProcess = null;
            }
        }
    }
} 