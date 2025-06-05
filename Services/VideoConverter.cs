using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace AutoStreamRec.Services
{
    public class VideoConverter
    {
        private readonly Action<string> _log;
        private readonly ExecutableLocator _locator;

        public VideoConverter(Action<string> logAction, ExecutableLocator locator)
        {
            _log = logAction;
            _locator = locator;
        }

        public async Task<bool> ConvertToMp4(string tsFilePath, string? mp4FilePath = null)
        {
            _log($"Début conversion: {tsFilePath}");

            if (!File.Exists(tsFilePath))
            {
                _log($"ERREUR: Fichier TS introuvable: {tsFilePath}");
                return false;
            }

            string ffmpegPath = _locator.FindExecutablePath("ffmpeg");
            if (string.IsNullOrEmpty(ffmpegPath))
            {
                _log("ERREUR: FFmpeg non trouvé !");
                return false;
            }

            mp4FilePath ??= Path.ChangeExtension(tsFilePath, ".mp4");
            _log($"Fichier MP4 de sortie: {mp4FilePath}");

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = $"-y -i \"{tsFilePath}\" -c:v copy -c:a aac -strict experimental \"{mp4FilePath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    _log("ERREUR: Impossible de démarrer FFmpeg");
                    return false;
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await process.WaitForExitAsync();
                _log($"FFmpeg terminé - Code de sortie: {process.ExitCode}");

                if (process.ExitCode != 0 || !File.Exists(mp4FilePath))
                {
                    _log("ERREUR: Conversion échouée ou fichier non créé.");
                    return false;
                }

                try
                {
                    File.Delete(tsFilePath);
                    _log("Fichier TS supprimé avec succès");
                }
                catch (Exception ex)
                {
                    _log($"AVERTISSEMENT: Impossible de supprimer le fichier TS: {ex.Message}");
                }

                return true;
            }
            catch (Exception ex)
            {
                _log($"ERREUR Conversion: {ex.Message}");
                return false;
            }
        }
    }
}
