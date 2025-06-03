using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace AutoStreamRec.Services
{
    public class FileHelper
    {
        private readonly Action<string> _log;

        public FileHelper(Action<string> logAction)
        {
            _log = logAction;
        }

        public async Task<bool> EnsureDirectoryWritableAsync(string outputDir)
        {
            try
            {
                Directory.CreateDirectory(outputDir);
                if (!Directory.Exists(outputDir))
                {
                    _log("ERREUR: Le dossier n'a pas été créé");
                    return false;
                }

                string testFile = Path.Combine(outputDir, "write_test.tmp");
                await File.WriteAllTextAsync(testFile, "test");
                File.Delete(testFile);

                _log("Dossier et permissions OK");
                return true;
            }
            catch (Exception ex)
            {
                _log($"ERREUR Dossier: {ex.Message}");
                _log($"Chemin complet tenté: {Path.GetFullPath(outputDir)}");
                return false;
            }
        }

        public string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "Unknown_Channel";

            var invalidChars = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name);

            foreach (var c in invalidChars)
                sb.Replace(c, '_');

            return sb.ToString().Trim();
        }
    }
}