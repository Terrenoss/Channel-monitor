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
    public partial class MainViewModel : ViewModelBase, IDisposable
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
        private volatile bool _isSurveillanceActive;
        private volatile bool _isRecording;
        private CancellationTokenSource _surveillanceCts;
        private bool _liveDetectedBySurveillance;
        private string _ignoredVideoId;
        private bool _hasLoggedIgnoredLive = false;
        private readonly object _stateLock = new object();
        private bool _disposed = false;

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
                lock (_stateLock)
                {
                    if (_isSurveillanceActive != value)
                    {
                        _isSurveillanceActive = value;
                        // Notifier les changements sur le thread UI
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            OnPropertyChanged(nameof(IsSurveillanceActive));
                            StartSurveillanceCommand.NotifyCanExecuteChanged();
                            StopSurveillanceCommand.NotifyCanExecuteChanged();
                            StartMonitoringCommand.NotifyCanExecuteChanged();
                        });
                    }
                }
            }
        }

        public bool IsRecording
        {
            get => _isRecording;
            set
            {
                lock (_stateLock)
                {
                    if (_isRecording != value)
                    {
                        _isRecording = value;
                        // Notifier les changements sur le thread UI
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            OnPropertyChanged(nameof(IsRecording));
                            StartMonitoringCommand.NotifyCanExecuteChanged();
                            StopMonitoringCommand.NotifyCanExecuteChanged();
                        });
                    }
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
            var logEntry = $"[{timestamp}] {message}";
            
            // Utiliser le dispatcher UI pour ajouter des logs de manière thread-safe
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                ActivityLogs.Add(logEntry);
                
                // Limiter le nombre de logs pour éviter la croissance mémoire
                const int MAX_LOGS = 1000;
                while (ActivityLogs.Count > MAX_LOGS)
                {
                    ActivityLogs.RemoveAt(0); // Supprimer le plus ancien
                }
            });
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
            ThrowIfDisposed();
            
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
                try
                {
                    while (!_surveillanceCts.IsCancellationRequested)
                    {
                        bool isCurrentlyRecording;
                        lock (_stateLock)
                        {
                            isCurrentlyRecording = _isRecording;
                        }
                        
                        if (isCurrentlyRecording)
                        {
                            // On ne log plus ce message pendant la conversion ou après l'arrêt
                            await Task.Delay(30000, _surveillanceCts.Token);
                            continue;
                        }
                        // On ne log "Détection du live..." que si le live n'est pas ignoré ou si l'état change
                        if (_ignoredVideoId == null)
                            AddLog("[Surveillance] Détection du live...");
                        bool isLive = await _streamDetector.IsLiveAsync(StreamUrl);
                        
                        lock (_stateLock)
                        {
                            isCurrentlyRecording = _isRecording;
                        }
                        
                        if (isLive && !isCurrentlyRecording)
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
                }
                catch (TaskCanceledException)
                {
                    _logger.LogInformation("Surveillance annulée proprement.");
                    AddLog("Surveillance arrêtée proprement.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erreur inattendue dans la surveillance");
                    AddLog($"Erreur inattendue dans la surveillance : {ex.Message}");
                }
            }, _surveillanceCts.Token);
        }

        [RelayCommand(CanExecute = nameof(CanStopSurveillance))]
        private async Task StopSurveillance()
        {
            ThrowIfDisposed();
            
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
            IsRecording = false;
        }

        [RelayCommand(CanExecute = nameof(CanStartMonitoring))]
        private async Task StartMonitoringAsync()
        {
            ThrowIfDisposed();
            
            try
            {
                if (string.IsNullOrEmpty(StreamUrl))
                {
                    AddLog("Erreur : L'URL du stream est vide");
                    return;
                }
                AddLog($"[Enregistrement] Démarrage de l'enregistrement (vérification API directe) : {StreamUrl}");
                IsRecording = true;
                var streamInfo = await _streamDetector.GetStreamInfoAsync(StreamUrl);
                if (streamInfo == null || !streamInfo.IsLive)
                {
                    AddLog("[Enregistrement] Erreur : Aucun live détecté (API)");
                    IsRecording = false;
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors du démarrage de l'enregistrement");
                AddLog($"Erreur lors du démarrage de l'enregistrement : {ex.Message}");
                IsRecording = false;
            }
        }

        [RelayCommand(CanExecute = nameof(CanStopMonitoring))]
        private async Task StopMonitoring()
        {
            ThrowIfDisposed();
            
            if (_streamRecorder != null && !string.IsNullOrEmpty(_streamRecorder.CurrentStreamId))
            {
                _ignoredVideoId = _streamRecorder.CurrentStreamId;
                _hasLoggedIgnoredLive = true; // On considère le live comme ignoré immédiatement
                AddLog($"Arrêt manuel : le live {_ignoredVideoId} sera ignoré par la surveillance tant qu'il ne change pas.");
            }
            else
            {
                AddLog("Arrêt manuel : impossible de déterminer l'ID du live à ignorer.");
            }
            await _streamRecorder.StopRecordingAsync();
            IsRecording = false;
            // Mise à jour immédiate de l'état pour réactiver le bouton Démarrer
            LiveDetectedBySurveillance = false;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                OnPropertyChanged(nameof(CanStartMonitoring));
                StartMonitoringCommand.NotifyCanExecuteChanged();
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
                    await RefreshTempFoldersAsync();
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
        public async Task RefreshTempFoldersAsync()
        {
            try
            {
                TempFoldersWithTs.Clear();
                var recordingsRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "Strivea",
                    "Recordings");
                
                if (!Directory.Exists(recordingsRoot)) return;
                
                // Rendre la recherche asynchrone pour ne pas bloquer l'UI
                await Task.Run(() =>
                {
                    var tempDirs = Directory.GetDirectories(recordingsRoot, "Temp", SearchOption.AllDirectories);
                    var foldersWithTs = new List<string>();
                    
                    foreach (var dir in tempDirs)
                    {
                        // Recherche récursive de tous les .ts dans Temp et ses sous-dossiers
                        var tsFiles = Directory.GetFiles(dir, "*.ts", SearchOption.AllDirectories);
                        if (tsFiles.Any())
                        {
                            foldersWithTs.Add(dir);
                        }
                    }
                    
                    // Mettre à jour l'UI sur le thread principal
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        TempFoldersWithTs.Clear();
                        foreach (var folder in foldersWithTs)
                        {
                            TempFoldersWithTs.Add(folder);
                        }
                    });
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors du rafraîchissement des dossiers temporaires");
                AddLog($"Erreur lors du rafraîchissement : {ex.Message}");
            }
        }

        public void RefreshTempFolders()
        {
            // Méthode synchrone pour la compatibilité, mais elle appelle la version asynchrone
            _ = RefreshTempFoldersAsync();
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

                // Supprimer le dossier et le recréer
                Directory.Delete(tempDir, true);
                
                // Vérifier que la suppression a réussi
                if (Directory.Exists(tempDir))
                {
                    throw new IOException($"Impossible de supprimer complètement le dossier {tempDir}");
                }
                
                // Recréer le dossier vide
                Directory.CreateDirectory(tempDir);
                
                // Vérifier que la création a réussi
                if (!Directory.Exists(tempDir))
                {
                    throw new IOException($"Impossible de recréer le dossier {tempDir}");
                }

                AddLog($"Suppression terminée : {total} éléments supprimés dans Temp.");
                await RefreshTempFoldersAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors du nettoyage des fichiers temporaires");
                AddLog($"Erreur lors du nettoyage : {ex.Message}");
                
                // Vérifier l'état du dossier après erreur
                if (Directory.Exists(tempDir))
                {
                    AddLog("Le dossier Temp existe toujours. Vérifiez manuellement son contenu.");
                }
                else
                {
                    AddLog("Le dossier Temp a été supprimé mais n'a pas pu être recréé.");
                }
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
            await RefreshTempFoldersAsync();
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

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(MainViewModel));
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                try
                {
                    // Arrêter la surveillance
                    _surveillanceCts?.Cancel();
                    _surveillanceCts?.Dispose();
                    
                    // Arrêter le monitoring
                    _monitoringCts?.Cancel();
                    _monitoringCts?.Dispose();
                    
                    // Arrêter l'enregistrement et disposer le StreamRecorder
                    if (_streamRecorder != null)
                    {
                        try
                        {
                            if (_streamRecorder.IsRecording)
                            {
                                _streamRecorder.StopRecordingAsync().Wait(5000); // 5 secondes max
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogWarning(ex, "Erreur lors de l'arrêt de l'enregistrement lors du dispose");
                        }
                        finally
                        {
                            _streamRecorder.Dispose();
                        }
                    }
                    
                    // Disposer le StreamDetector
                    if (_streamDetector is IDisposable disposableDetector)
                    {
                        try
                        {
                            disposableDetector.Dispose();
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogWarning(ex, "Erreur lors du dispose du StreamDetector");
                        }
                    }
                    
                    // Disposer les autres services si disponibles
                    var serviceProvider = App.ServiceProvider;
                    if (serviceProvider != null)
                    {
                        try
                        {
                            var executableLocator = serviceProvider.GetService<IExecutableLocator>() as IDisposable;
                            executableLocator?.Dispose();
                            
                            var statsLogger = serviceProvider.GetService<RecordingStatsLogger>() as IDisposable;
                            statsLogger?.Dispose();
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogWarning(ex, "Erreur lors du dispose des services");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Erreur lors du dispose de MainViewModel");
                }
                finally
                {
                    _disposed = true;
                }
            }
        }

        ~MainViewModel()
        {
            Dispose(false);
        }
    }
}
