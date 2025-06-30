using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Microsoft.Extensions.Logging;
using Strivea.ViewModels;

namespace Strivea.Services
{
    public class LogService
    {
        private readonly ILogger<LogService> _logger;
        private readonly string _logFilePath;
        private readonly MainViewModel _viewModel;

        public LogService(ILogger<LogService> logger, MainViewModel viewModel)
        {
            _logger = logger;
            _viewModel = viewModel;
            _logFilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Strivea",
                "logs",
                $"log_{DateTime.Now:yyyy-MM}.txt"
            );
        }

        public void LogInfo(string message)
        {
            _logger.LogInformation(message);
            _viewModel.StatusMessage = message;
        }

        public void LogWarning(string message)
        {
            _logger.LogWarning(message);
            _viewModel.StatusMessage = $"⚠️ {message}";
        }

        public void LogError(string message, Exception ex = null)
        {
            if (ex != null)
            {
                _logger.LogError(ex, message);
            }
            else
            {
                _logger.LogError(message);
            }
            _viewModel.StatusMessage = $"❌ {message}";
        }

        public async Task CopyLogsToClipboard()
        {
            try
            {
                if (File.Exists(_logFilePath))
                {
                    var logs = await File.ReadAllTextAsync(_logFilePath);
                    if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    {
                        var clipboard = desktop.MainWindow?.Clipboard;
                        if (clipboard != null)
                        {
                            await clipboard.SetTextAsync(logs);
                            _logger.LogInformation("Logs copiés dans le presse-papiers");
                        }
                    }
                }
                else
                {
                    _logger.LogWarning("Aucun fichier de log trouvé");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la copie des logs");
            }
        }

        public void UpdateRecordingStats(string stats)
        {
            _viewModel.CurrentRecordingStats = stats;
        }
    }
} 