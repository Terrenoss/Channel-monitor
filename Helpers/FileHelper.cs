using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Strivea.Helpers
{
    public class FileHelper
    {
        private readonly ILogger<FileHelper> _logger;

        public FileHelper(ILogger<FileHelper> logger)
        {
            _logger = logger;
        }

        public string EnsureDirectoryExists(string path)
        {
            try
            {
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                    _logger.LogInformation($"Répertoire créé : {path}");
                }
                return path;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erreur lors de la création du répertoire : {path}");
                throw;
            }
        }

        public string GetUniqueFileName(string directory, string baseName, string extension)
        {
            var fileName = $"{baseName}{extension}";
            var fullPath = Path.Combine(directory, fileName);
            var counter = 1;

            while (File.Exists(fullPath))
            {
                fileName = $"{baseName}_{counter}{extension}";
                fullPath = Path.Combine(directory, fileName);
                counter++;
            }

            return fileName;
        }

        public void DeleteFileIfExists(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    _logger.LogInformation($"Fichier supprimé : {filePath}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erreur lors de la suppression du fichier : {filePath}");
                throw;
            }
        }

        public long GetFileSize(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    return new FileInfo(filePath).Length;
                }
                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erreur lors de la récupération de la taille du fichier : {filePath}");
                return 0;
            }
        }

        public string SanitizeFileName(string fileName)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = fileName;
            foreach (var c in invalidChars)
            {
                sanitized = sanitized.Replace(c, '_');
            }
            return sanitized;
        }

        public async Task EnsureDirectoryWritableAsync(string path)
        {
            try
            {
                var testFile = Path.Combine(path, "test.tmp");
                await File.WriteAllTextAsync(testFile, "test");
                File.Delete(testFile);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Le répertoire {path} n'est pas accessible en écriture");
                throw;
            }
        }
    }
} 