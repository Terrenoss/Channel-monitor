using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using System.IO;
using Avalonia.Controls;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Strivea.Services;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using System.Collections.Generic;
using System.Linq;
using Strivea.Views;

namespace Strivea.ViewModels
{
    public partial class MainViewModel : ViewModelBase
    {
        private readonly ILogger<MainViewModel> _logger;
        private readonly IStreamDetector _streamDetector;
        private readonly StreamRecorder _streamRecorder;
        private string _streamUrl;
        private string _outputDirectory;
        private bool _isMonitoring;
        private string _statusMessage;
        private bool _isWorking;
        private string _currentAction;
        private string _currentRecordingStats;
        private CancellationTokenSource _monitoringCts;
        private bool _isTempSectionVisible;

        public MainViewModel(
            ILogger<MainViewModel> logger,
            IStreamDetector streamDetector,
            IServiceProvider serviceProvider)
        {
            _logger = logger;
            _streamDetector = streamDetector;
            ActivityLogs = new ObservableCollection<string>();
            OutputDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Strivea",
                "Recordings");

            // Création manuelle du StreamRecorder avec les bons callbacks UI
            var loggerStreamRecorder = serviceProvider.GetRequiredService<ILogger<StreamRecorder>>();
            var executableLocator = serviceProvider.GetRequiredService<IExecutableLocator>();
            var statsLogger = serviceProvider.GetRequiredService<RecordingStatsLogger>();
            _streamRecorder = new StreamRecorder(
                loggerStreamRecorder,
                executableLocator,
                statsLogger,
                status => StatusMessage = status,
                log => AddLog(log)
            );

            _logger.LogInformation("MainViewModel initialisé");
            AddLog("Application démarrée");

            _logger.LogInformation($"CanStartMonitoring après changement: {CanStartMonitoring}");
            _logger.LogInformation($"CanStopMonitoring après changement: {CanStopMonitoring}");

            OpenTempFilesManagerCommand = new RelayCommand(OpenTempFilesManager);
        }

        public ObservableCollection<string> ActivityLogs { get; }

        public string StreamUrl
        {
            get => _streamUrl;
            set => SetProperty(ref _streamUrl, value);
        }

        public string OutputDirectory
        {
            get => _outputDirectory;
            set => SetProperty(ref _outputDirectory, value);
        }

        public bool IsMonitoring
        {
            get => _isMonitoring;
            set
            {
                if (SetProperty(ref _isMonitoring, value))
                {
                    _logger.LogInformation($"IsMonitoring a changé: {value}");
                    StartMonitoringCommand.NotifyCanExecuteChanged();
                    StopMonitoringCommand.NotifyCanExecuteChanged();
                    _logger.LogInformation($"CanStartMonitoring après changement: {CanStartMonitoring}");
                    _logger.LogInformation($"CanStopMonitoring après changement: {CanStopMonitoring}");
                }
            }
        }

        public bool CanStartMonitoring => !IsMonitoring;
        public bool CanStopMonitoring
        {
            get
            {
                _logger.LogInformation($"CanStopMonitoring est évalué. IsMonitoring: {IsMonitoring}");
                return IsMonitoring;
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public bool IsWorking
        {
            get => _isWorking;
            set => SetProperty(ref _isWorking, value);
        }

        public string CurrentAction
        {
            get => _currentAction;
            set => SetProperty(ref _currentAction, value);
        }

        public string CurrentRecordingStats
        {
            get => _currentRecordingStats;
            set => SetProperty(ref _currentRecordingStats, value);
        }

        public ObservableCollection<string> TempFoldersWithTs { get; } = new();

        public bool IsTempSectionVisible
        {
            get => _isTempSectionVisible;
            set => SetProperty(ref _isTempSectionVisible, value);
        }

        public IRelayCommand OpenTempFilesManagerCommand { get; }

        private void AddLog(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            ActivityLogs.Add($"[{timestamp}] {message}");
        }

        [RelayCommand]
        private async Task CheckDependenciesAsync()
        {
            try
            {
                IsWorking = true;
                CurrentAction = "Vérification des dépendances";
                StatusMessage = "Vérification des dépendances...";
                _logger.LogInformation("Vérification des dépendances...");
                AddLog("Vérification des dépendances...");
                await Task.Delay(1000); // Simulation
                AddLog("FFmpeg: OK");
                AddLog("Streamlink: OK");
                StatusMessage = "Vérification terminée";
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur lors de la vérification des dépendances";
                _logger.LogError(ex, "Erreur lors de la vérification des dépendances");
                AddLog($"Erreur : {ex.Message}");
            }
            finally
            {
                IsWorking = false;
            }
        }

        [RelayCommand(CanExecute = nameof(CanStartMonitoring))]
        private async Task StartMonitoringAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(StreamUrl))
                {
                    AddLog("Erreur : L'URL du stream est vide");
                    return;
                }

                AddLog($"Démarrage de la surveillance : {StreamUrl}");
                AddLog($"Vérification du statut en direct pour {StreamUrl}");

                var streamInfo = await _streamDetector.GetStreamInfoAsync(StreamUrl);
                if (streamInfo == null)
                {
                    AddLog("Erreur : Impossible de récupérer les informations du stream");
                    return;
                }

                AddLog($"État du stream : {(streamInfo.IsLive ? "En direct" : "Hors ligne")}");

                if (streamInfo.IsLive)
                {
                    AddLog("Stream en direct détecté, démarrage de l'enregistrement...");
                    var streamUrlResult = await _streamDetector.GetStreamUrlAsync(StreamUrl);
                    if (string.IsNullOrEmpty(streamUrlResult))
                    {
                        AddLog("Erreur : Impossible de récupérer l'URL du stream");
                        return;
                    }

                    var platform = "YouTube"; // Pour l'instant, on ne gère que YouTube
                    var channelName = streamInfo.ChannelName;
                    var streamTitle = streamInfo.Title;

                    AddLog($"Démarrage de l'enregistrement pour {channelName} sur {platform}");
                    AddLog($"Titre du stream : {streamTitle}");
                    AddLog("URL du stream récupérée avec succès");

                    _monitoringCts = new CancellationTokenSource();
                    IsMonitoring = true;
                    await _streamRecorder.StartRecordingAsync(
                        streamUrlResult,
                        platform,
                        channelName,
                        streamTitle,
                        _monitoringCts.Token);

                    AddLog("Surveillance démarrée avec succès");
                }
                else
                {
                    AddLog("Le stream n'est pas en direct");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors du démarrage de la surveillance");
                AddLog($"Erreur : {ex.Message}");
            }
        }

        [RelayCommand(CanExecute = nameof(CanStopMonitoring))]
        private async Task StopMonitoringAsync()
        {
            try
            {
                AddLog("Arrêt de la surveillance...");
                _monitoringCts?.Cancel();
                await _streamRecorder.StopRecordingAsync();
                AddLog("Surveillance arrêtée avec succès");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'arrêt de la surveillance");
                AddLog($"Erreur lors de l'arrêt : {ex.Message}");
            }
            finally
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    IsMonitoring = false;
                    _logger.LogInformation($"[DEBUG] IsMonitoring forcé à {IsMonitoring} dans finally StopMonitoringAsync");
                    StartMonitoringCommand.NotifyCanExecuteChanged();
                    StopMonitoringCommand.NotifyCanExecuteChanged();
                });
            }
        }

        [RelayCommand]
        private async Task CleanupAsync()
        {
            try
            {
                IsWorking = true;
                CurrentAction = "Nettoyage";
                StatusMessage = "Nettoyage en cours...";
                _logger.LogInformation("Nettoyage en cours...");
                AddLog("Nettoyage en cours...");
                await Task.Delay(1000); // Simulation
                StatusMessage = "Nettoyage terminé";
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur lors du nettoyage";
                _logger.LogError(ex, "Erreur lors du nettoyage");
                AddLog($"Erreur : {ex.Message}");
            }
            finally
            {
                IsWorking = false;
            }
        }

        [RelayCommand]
        public async Task CleanTempFiles()
        {
            var result = await ShowConfirmationDialog(
                "Nettoyer les fichiers temporaires",
                "Êtes-vous sûr de vouloir supprimer tous les fichiers .ts temporaires ?\n\nATTENTION : Cette action supprimera tous les segments vidéo temporaires (.ts) pour cette chaîne. Vous ne pourrez plus générer un seul fichier mp4 à partir de plusieurs sessions. Assurez-vous que le live est bien terminé et que vous n'aurez plus besoin de reprendre l'enregistrement."
            );
            if (!result)
            {
                AddLog("Nettoyage annulé par l'utilisateur.");
                return;
            }
            try
            {
                var tempDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "Strivea",
                    "Recordings",
                    "YouTube", // À adapter si multi-plateforme
                    _streamRecorder?.SanitizeFileName(_streamRecorder?.ChannelName ?? ""),
                    "Temp");
                if (Directory.Exists(tempDir))
                {
                    var tsFiles = Directory.GetFiles(tempDir, "*.ts");
                    int count = 0;
                    foreach (var ts in tsFiles)
                    {
                        File.Delete(ts);
                        count++;
                    }
                    AddLog($"{count} fichiers .ts supprimés dans {tempDir}");
                }
                else
                {
                    AddLog("Aucun dossier Temp trouvé pour cette chaîne.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors du nettoyage des fichiers .ts");
                AddLog($"Erreur lors du nettoyage : {ex.Message}");
            }
        }

        [RelayCommand]
        public void RefreshTempFolders()
        {
            TempFoldersWithTs.Clear();
            var recordingsRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Strivea",
                "Recordings");
            if (!Directory.Exists(recordingsRoot)) return;
            var tempDirs = Directory.GetDirectories(recordingsRoot, "Temp", SearchOption.AllDirectories);
            foreach (var dir in tempDirs)
            {
                if (Directory.GetFiles(dir, "*.ts").Any())
                {
                    TempFoldersWithTs.Add(dir);
                }
            }
        }

        [RelayCommand]
        public async Task DeleteTempFilesForFolder((string tempDir, Window parentWindow) param)
        {
            var tempDir = param.tempDir;
            var parentWindow = param.parentWindow;
            if (string.IsNullOrWhiteSpace(tempDir) || !Directory.Exists(tempDir))
            {
                AddLog("Dossier Temp introuvable.");
                return;
            }
            var channelName = Directory.GetParent(tempDir)?.Parent?.Name ?? tempDir;
            var result = await ShowConfirmationDialog(
                $"Supprimer les fichiers temporaires pour {channelName}",
                $"Êtes-vous sûr de vouloir supprimer tous les fichiers .ts du dossier Temp de la chaîne '{channelName}' ?\n\nATTENTION : Cette action supprimera tous les segments vidéo temporaires (.ts) pour cette chaîne.\nVous ne pourrez plus générer un seul fichier mp4 à partir de plusieurs sessions.\nAssurez-vous que le live est bien terminé et que vous n'aurez plus besoin de reprendre l'enregistrement.",
                parentWindow
            );
            if (!result)
            {
                AddLog("Suppression annulée par l'utilisateur.");
                return;
            }
            try
            {
                var tsFiles = Directory.GetFiles(tempDir, "*.ts");
                int count = 0;
                foreach (var ts in tsFiles)
                {
                    File.Delete(ts);
                    count++;
                }
                AddLog($"{count} fichiers .ts supprimés dans {tempDir}");
                RefreshTempFolders();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors du nettoyage des fichiers .ts");
                AddLog($"Erreur lors du nettoyage : {ex.Message}");
            }
        }

        [RelayCommand]
        public void ToggleTempSectionVisibility()
        {
            IsTempSectionVisible = !IsTempSectionVisible;
        }

        private async Task<bool> ShowConfirmationDialog(string title, string message, Window? parent = null)
        {
            var window = parent ?? (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (window == null)
                return false;
            var box = MessageBoxManager.GetMessageBoxStandard(title, message, ButtonEnum.YesNo, Icon.Warning);
            var result = await box.ShowWindowDialogAsync(window);
            return result == ButtonResult.Yes;
        }

        private async void OpenTempFilesManager()
        {
            RefreshTempFolders();
            var window = new TempFilesManagerWindow();
            window.DataContext = this;
            if (App.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                var mainWindow = desktop.MainWindow;
                await window.ShowDialog(mainWindow);
            }
            else
            {
                await window.ShowDialog(null);
            }
        }

        [RelayCommand]
        private async Task BrowseOutputDirectoryAsync()
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Sélectionner le dossier de destination"
            };
            // On tente de trouver la fenêtre principale Avalonia
            var topLevel = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;
            if (topLevel != null)
            {
                var result = await dialog.ShowAsync(topLevel);
                if (!string.IsNullOrWhiteSpace(result))
                {
                    OutputDirectory = result;
                }
            }
        }
    }
}
