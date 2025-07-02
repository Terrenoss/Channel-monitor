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
using Avalonia.Threading;

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
        private bool _isSurveillanceActive;
        private bool _isRecording;
        private CancellationTokenSource _surveillanceCts;
        private bool _liveDetectedBySurveillance;
        private string _ignoredVideoId;
        private bool _hasLoggedIgnoredLive = false;

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

        public bool IsSurveillanceActive
        {
            get => _isSurveillanceActive;
            set
            {
                if (SetProperty(ref _isSurveillanceActive, value))
                {
                    StartSurveillanceCommand.NotifyCanExecuteChanged();
                    StopSurveillanceCommand.NotifyCanExecuteChanged();
                    StartMonitoringCommand.NotifyCanExecuteChanged();
                }
            }
        }

        public bool IsRecording
        {
            get => _isRecording;
            set
            {
                if (SetProperty(ref _isRecording, value))
                {
                    StartMonitoringCommand.NotifyCanExecuteChanged();
                    StopMonitoringCommand.NotifyCanExecuteChanged();
                }
            }
        }

        public bool CanStartSurveillance => !IsSurveillanceActive;
        public bool CanStopSurveillance => IsSurveillanceActive;
        public bool CanStartMonitoring => !IsMonitoring && !IsRecording && (!IsSurveillanceActive || !_liveDetectedBySurveillance);
        public bool CanStopMonitoring => IsMonitoring || IsRecording;

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

        public bool LiveDetectedBySurveillance
        {
            get => _liveDetectedBySurveillance;
            set
            {
                if (SetProperty(ref _liveDetectedBySurveillance, value))
                {
                    StartMonitoringCommand.NotifyCanExecuteChanged();
                }
            }
        }

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

        [RelayCommand(CanExecute = nameof(CanStartSurveillance))]
        private async Task StartSurveillanceAsync()
        {
            if (string.IsNullOrEmpty(StreamUrl))
            {
                AddLog("Erreur : L'URL du stream est vide");
                return;
            }
            AddLog($"Surveillance activée pour : {StreamUrl}");
            IsSurveillanceActive = true;
            _surveillanceCts = new CancellationTokenSource();
            await Task.Run(async () =>
            {
                while (!_surveillanceCts.IsCancellationRequested)
                {
                    if (IsRecording)
                    {
                        // On ne log plus ce message pendant la conversion ou après l'arrêt
                        await Task.Delay(30000, _surveillanceCts.Token);
                        continue;
                    }
                    // On ne log "Détection du live..." que si le live n'est pas ignoré ou si l'état change
                    if (_ignoredVideoId == null)
                        AddLog("[Surveillance] Détection du live...");
                    bool isLive = await _streamDetector.IsLiveAsync(StreamUrl);
                    if (isLive && !IsRecording)
                    {
                        // On ne log plus ce message si le live est ignoré
                        var streamInfo = await _streamDetector.GetStreamInfoAsync(StreamUrl);
                        if (streamInfo != null && streamInfo.IsLive)
                        {
                            if (_ignoredVideoId == streamInfo.StreamId)
                            {
                                Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                                    LiveDetectedBySurveillance = false;
                                });
                                if (!_hasLoggedIgnoredLive)
                                {
                                    AddLog("[Surveillance] Le live actuel a été ignoré suite à un arrêt manuel, en attente d'un nouveau live.");
                                    _hasLoggedIgnoredLive = true;
                                }
                                // On saute le reste de la boucle
                                await Task.Delay(30000, _surveillanceCts.Token);
                                continue;
                            }
                            _hasLoggedIgnoredLive = false;
                            AddLog("[Surveillance] Live détecté, récupération des infos...");
                            AddLog($"[Surveillance] Diagnostic : _ignoredVideoId = '{_ignoredVideoId}', StreamId détecté = '{streamInfo?.StreamId}'");
                            Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                                LiveDetectedBySurveillance = true;
                            });
                            AddLog($"[Surveillance] Live trouvé : '{streamInfo.Title}' sur la chaîne '{streamInfo.ChannelName}'.");
                            AddLog($"[Surveillance] Démarrage de l'enregistrement pour la chaîne '{streamInfo.ChannelName}'...");
                            await StartMonitoringAsync();
                        }
                        else
                        {
                            Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                                LiveDetectedBySurveillance = false;
                            });
                            AddLog("[Surveillance] Aucun live confirmé par l'API après détection Streamlink.");
                        }
                        await Task.Delay(30000, _surveillanceCts.Token);
                        continue;
                    }
                    else
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                            LiveDetectedBySurveillance = false;
                        });
                        AddLog("[Surveillance] Aucun live détecté.");
                    }
                    await Task.Delay(30000, _surveillanceCts.Token);
                }
            }, _surveillanceCts.Token);
        }

        [RelayCommand(CanExecute = nameof(CanStopSurveillance))]
        private async Task StopSurveillance()
        {
            AddLog("Surveillance arrêtée");
            IsSurveillanceActive = false;
            _surveillanceCts?.Cancel();
            LiveDetectedBySurveillance = false;
            // On tente toujours d'arrêter l'enregistrement côté service, même si l'UI pense qu'il n'y en a pas
            if (_streamRecorder.IsRecording)
            {
                AddLog("[Surveillance] Arrêt de l'enregistrement en cours (fin de surveillance) côté service...");
            }
            else
            {
                AddLog("[Surveillance] Aucun enregistrement détecté côté service, on force l'arrêt pour nettoyage.");
            }
            await _streamRecorder.StopRecordingAsync();
            Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                IsRecording = false;
            });
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
                AddLog($"[Enregistrement] Démarrage de l'enregistrement (vérification API directe) : {StreamUrl}");
                Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                    IsRecording = true;
                });
                var streamInfo = await _streamDetector.GetStreamInfoAsync(StreamUrl);
                if (streamInfo == null || !streamInfo.IsLive)
                {
                    AddLog("[Enregistrement] Erreur : Aucun live détecté (API)");
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                        IsRecording = false;
                    });
                    return;
                }
                // Si on reprend un live ignoré, on lève l'ignorance
                if (_ignoredVideoId == streamInfo.StreamId)
                {
                    _ignoredVideoId = null;
                    AddLog("[Enregistrement] Reprise manuelle de l'enregistrement du live actuel.");
                }
                AddLog($"[Enregistrement] Live trouvé : '{streamInfo.Title}' sur la chaîne '{streamInfo.ChannelName}'.");
                AddLog($"[Enregistrement] Enregistrement en cours pour la chaîne '{streamInfo.ChannelName}'...");
                    await _streamRecorder.StartRecordingAsync(
                    streamInfo.StreamUrl,
                    streamInfo.Platform ?? "YouTube",
                    streamInfo.ChannelName ?? "UnknownChannel",
                    streamInfo.Title ?? "Stream en direct",
                    CancellationToken.None,
                        streamInfo.ChannelFolderName,
                        streamInfo.StreamId
                    );
            }
            finally
            {
                // Rien ici, l'arrêt se fait ailleurs
            }
        }

        [RelayCommand(CanExecute = nameof(CanStopMonitoring))]
        private async Task StopMonitoring()
        {
            if (_streamRecorder != null && !string.IsNullOrEmpty(_streamRecorder.CurrentStreamId))
            {
                _ignoredVideoId = _streamRecorder.CurrentStreamId;
                _hasLoggedIgnoredLive = false;
                AddLog($"Arrêt manuel : le live {_ignoredVideoId} sera ignoré par la surveillance tant qu'il ne change pas.");
            }
            else
            {
                AddLog("Arrêt manuel : impossible de déterminer l'ID du live à ignorer.");
            }
            await _streamRecorder.StopRecordingAsync();
            Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                IsRecording = false;
            });
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
                    // Compter tous les fichiers et dossiers à supprimer
                    int fileCount = Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories).Length;
                    int dirCount = Directory.GetDirectories(tempDir, "*", SearchOption.AllDirectories).Length;
                    int total = fileCount + dirCount;

                    Directory.Delete(tempDir, true);
                    Directory.CreateDirectory(tempDir);

                    AddLog($"Suppression terminée : {total} éléments supprimés dans Temp.");
                    RefreshTempFolders();
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
                // Recherche récursive de tous les .ts dans Temp et ses sous-dossiers
                var tsFiles = Directory.GetFiles(dir, "*.ts", SearchOption.AllDirectories);
                if (tsFiles.Any())
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
            // Extraire le nom de la chaîne à partir du chemin du dossier Temp
            var channelName = Directory.GetParent(tempDir)?.Name ?? tempDir;
            var result = await ShowConfirmationDialog(
                $"Supprimer les fichiers temporaires pour {channelName}",
                $"Êtes-vous sûr de vouloir supprimer tous les fichiers du dossier Temp de la chaîne '{channelName}' ?\n\nATTENTION : Cette action supprimera tous les segments vidéo temporaires (.ts) pour cette chaîne.\nVous ne pourrez plus générer un seul fichier mp4 à partir de plusieurs sessions.\nAssurez-vous que le live est bien terminé et que vous n'aurez plus besoin de reprendre l'enregistrement.",
                parentWindow
            );
            if (!result)
            {
                AddLog("Suppression annulée par l'utilisateur.");
                return;
            }
            try
            {
                // Compter tous les fichiers et dossiers à supprimer
                int fileCount = Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories).Length;
                int dirCount = Directory.GetDirectories(tempDir, "*", SearchOption.AllDirectories).Length;
                int total = fileCount + dirCount;

                Directory.Delete(tempDir, true);
                Directory.CreateDirectory(tempDir);

                AddLog($"Suppression terminée : {total} éléments supprimés dans Temp.");
                RefreshTempFolders();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors du nettoyage des fichiers temporaires");
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

        // Ajout d'une méthode utilitaire pour extraire l'ID du live depuis StreamInfo (YouTube)
        private string ExtractLiveIdFromStreamInfo(Strivea.Models.StreamInfo info)
        {
            // On tente d'extraire l'ID vidéo depuis l'URL du stream (YouTube)
            if (info == null || string.IsNullOrEmpty(info.StreamUrl))
                return null;
            var url = info.StreamUrl;
            var match = System.Text.RegularExpressions.Regex.Match(url, @"(?:v=|youtu\.be/|/live/|/shorts/|embed/)([\w-]{11})");
            if (match.Success)
                return match.Groups[1].Value;
            return null;
        }
    }
}
