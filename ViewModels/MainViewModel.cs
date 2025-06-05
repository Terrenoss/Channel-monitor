using AutoStreamRec.Services;
using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;
using System.IO;

namespace AutoStreamRec.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly StreamRecorderService _recorderService;
        private readonly ILogger<MainViewModel> _logger;
        private CancellationTokenSource? _cancellationTokenSource;
        private string _streamUrl;
        private bool _isDebugMode;
        private bool _isMonitoring;
        private bool _isWorking;
        private bool _isRecording;
        private string _currentAction;
        private string _currentRecordingStats;
        private string _statusMessage;
        private ObservableCollection<string> _activityLogs;
        private Task? _monitoringTask;
        private string _lastRequestedQuality = null;
        private string _lastDetectedQuality = null;
        private bool _surveillanceStoppedLogged = false;

        public string StreamUrl
        {
            get => _streamUrl;
            set
            {
                _streamUrl = value;
                OnPropertyChanged();
            }
        }

        public bool IsDebugMode
        {
            get => _isDebugMode;
            set
            {
                _isDebugMode = value;
                OnPropertyChanged();
            }
        }

        public bool IsMonitoring
        {
            get => _isMonitoring;
            set
            {
                _isMonitoring = value;
                OnPropertyChanged();
            }
        }

        public bool IsWorking
        {
            get => _isWorking;
            set
            {
                _isWorking = value;
                OnPropertyChanged();
            }
        }

        public bool IsRecording
        {
            get => _isRecording;
            set
            {
                _isRecording = value;
                OnPropertyChanged();
            }
        }

        public string CurrentAction
        {
            get => _currentAction;
            set
            {
                _currentAction = value;
                OnPropertyChanged();
            }
        }

        public string CurrentRecordingStats
        {
            get => _currentRecordingStats;
            set
            {
                _currentRecordingStats = value;
                OnPropertyChanged();
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                _statusMessage = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<string> ActivityLogs
        {
            get => _activityLogs;
            set
            {
                _activityLogs = value;
                OnPropertyChanged();
            }
        }

        public ICommand MonitorCommand { get; }
        public ICommand StopMonitoringCommand { get; }
        public ICommand CopyLogsCommand { get; }
        public ICommand ClearLogsCommand { get; }

        public MainViewModel(StreamRecorderService recorderService, ILogger<MainViewModel> logger)
        {
            _recorderService = recorderService;
            _logger = logger;
            _activityLogs = new ObservableCollection<string>();
            
            MonitorCommand = new RelayCommand(StartMonitoring, _ => !IsMonitoring);
            StopMonitoringCommand = new RelayCommand(StopMonitoring, _ => IsMonitoring);
            CopyLogsCommand = new RelayCommand(CopyLogs);
            ClearLogsCommand = new RelayCommand(ClearLogs);

            StatusMessage = "Prêt";
        }

        private async void StartMonitoring(object? parameter)
        {
            if (string.IsNullOrWhiteSpace(StreamUrl))
            {
                AddLog("Erreur: Veuillez entrer une URL de stream");
                return;
            }

            try
            {
                IsMonitoring = true;
                IsWorking = true;
                CurrentAction = "Démarrage de la surveillance...";
                StatusMessage = "Surveillance en cours";

                // Créer un nouveau CancellationTokenSource
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = new CancellationTokenSource();

                AddLog($"Démarrage de la surveillance pour: {StreamUrl}");

                // Démarrer la surveillance dans une tâche séparée
                _monitoringTask = Task.Run(async () =>
                {
                    try
                    {
                        await _recorderService.StartMonitoringAsync(StreamUrl, _cancellationTokenSource.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        AddLog("Surveillance annulée");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Erreur lors de la surveillance");
                        AddLog($"Erreur: {ex.Message}");
                    }
                    finally
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            IsMonitoring = false;
                            IsWorking = false;
                            IsRecording = false;
                            CurrentAction = string.Empty;
                            CurrentRecordingStats = string.Empty;
                            StatusMessage = "Surveillance arrêtée";
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors du démarrage de la surveillance");
                AddLog($"Erreur: {ex.Message}");
                StopMonitoring(null);
            }
        }

        private async void StopMonitoring(object? parameter)
        {
            try
            {
                if (_cancellationTokenSource != null)
                {
                    _cancellationTokenSource.Cancel();
                    _recorderService.StopMonitoring();
                    
                    if (_monitoringTask != null)
                    {
                        await _monitoringTask;
                    }

                    _cancellationTokenSource.Dispose();
                    _cancellationTokenSource = null;
                }

                IsMonitoring = false;
                StatusMessage = "Surveillance arrêtée";
                AddLog("Surveillance arrêtée");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'arrêt de la surveillance");
                AddLog($"Erreur lors de l'arrêt: {ex.Message}");
            }
            finally
            {
                IsWorking = false;
                CurrentAction = string.Empty;
            }
        }

        private void CopyLogs(object? parameter)
        {
            try
            {
                var logs = string.Join(Environment.NewLine, ActivityLogs);
                Clipboard.SetText(logs);
                StatusMessage = "Logs copiés dans le presse-papiers";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la copie des logs");
                AddLog($"Erreur lors de la copie des logs: {ex.Message}");
            }
        }

        private void ClearLogs(object? parameter)
        {
            if (Application.Current?.Dispatcher?.CheckAccess() == true)
            {
                ActivityLogs.Clear();
            }
            else
            {
                Application.Current?.Dispatcher?.Invoke(() => ActivityLogs.Clear());
            }
        }

        public void AddLog(string message)
        {
            void AddLogInternal(string msg)
            {
                // Filtrage des messages redondants de surveillance
                if (msg == "Surveillance arrêtée")
                {
                    if (_surveillanceStoppedLogged) return;
                    _surveillanceStoppedLogged = true;
                }
                else if (msg == "Surveillance annulée")
                {
                    if (_surveillanceStoppedLogged) return;
                    _surveillanceStoppedLogged = true;
                }
                else if (!msg.Contains("Surveillance"))
                {
                    _surveillanceStoppedLogged = false;
                }

                if (!IsDebugMode)
                {
                    // Filtrage des messages inutiles (identique à avant)
                    if (msg.Contains("[debug]") ||
                        msg.Contains("[cli][debug]") ||
                        msg.Contains("[plugins.youtube][debug]") ||
                        msg.Contains("[utils.l10n][debug]") ||
                        msg.Contains("[session][debug]") ||
                        msg.Contains("Dependencies:") ||
                        msg.Contains("Arguments:") ||
                        msg.Contains("Available streams:") ||
                        msg.Contains("Pre-buffering") ||
                        msg.Contains("Checking file output") ||
                        msg.Contains("Writing stream to output") ||
                        msg.Contains("consent data:") ||
                        msg.Contains("consent target:") ||
                        msg.Contains("Using video ID:") ||
                        msg.Contains("This video is live.") ||
                        msg.Contains("Language code:") ||
                        msg.Contains("Writing output to") ||
                        msg.Contains("Opening stream:") ||
                        msg.Contains("Found matching plugin") ||
                        msg.Contains("Dossier et permissions OK") ||
                        msg.Contains("Commande Streamlink:") ||
                        msg.Contains("[Streamlink]") ||
                        msg.StartsWith("Durée:") ||
                        msg.Contains("Stream détecté:  - ")
                    )
                    {
                        return;
                    }

                    // Détection de la qualité demandée
                    if (msg.StartsWith("Qualité demandée:"))
                    {
                        _lastRequestedQuality = msg.Substring("Qualité demandée:".Length).Trim();
                        var timestamp = DateTime.Now.ToString("HH:mm:ss");
                        var logMessage = $"[{timestamp}] Enregistrement démarré en qualité : {_lastRequestedQuality}";
                        ActivityLogs.Insert(0, logMessage);
                        if (ActivityLogs.Count > 1000) ActivityLogs.RemoveAt(ActivityLogs.Count - 1);
                        return;
                    }
                    // Détection de la qualité réelle
                    if (msg.StartsWith("Qualité réelle détectée :"))
                    {
                        _lastDetectedQuality = msg.Substring("Qualité réelle détectée :".Length).Trim();
                        var timestamp = DateTime.Now.ToString("HH:mm:ss");
                        var logMessage = $"[{timestamp}] Qualité sélectionnée : {_lastDetectedQuality}";
                        ActivityLogs.Insert(0, logMessage);
                        if (ActivityLogs.Count > 1000) ActivityLogs.RemoveAt(ActivityLogs.Count - 1);
                        return;
                    }
                    // Remplacement des messages utilisateur
                    if (msg.Contains("Stream détecté:"))
                    {
                        msg = "Stream en direct détecté";
                    }
                    else if (msg.Contains("Qualité demandée:"))
                    {
                        msg = "Enregistrement en qualité maximale";
                    }
                }
                else
                {
                    // Mode debug : filtrage minimal
                    if (msg.Contains("Pre-buffering") ||
                        msg.Contains("Writing stream to output") ||
                        msg.Contains("Checking file output") ||
                        msg.Contains("consent data:") ||
                        msg.Contains("consent target:") ||
                        msg.Contains("Language code:") ||
                        msg.Contains("Writing output to") ||
                        msg.Contains("Opening stream:") ||
                        msg.Contains("Found matching plugin") ||
                        msg.StartsWith("Durée:")
                    )
                    {
                        return;
                    }
                    if (msg.Contains("[Streamlink]"))
                    {
                        var path = msg.Split(']')[1].Trim();
                        var directory = Path.GetDirectoryName(path);
                        if (!string.IsNullOrWhiteSpace(directory))
                        {
                            msg = $"[Streamlink] Fichier enregistré dans : {directory}";
                        }
                        else
                        {
                            return; // Ne rien afficher si le chemin est vide
                        }
                    }
                }

                var ts = DateTime.Now.ToString("HH:mm:ss");
                var logMsg = $"[{ts}] {msg}";
                ActivityLogs.Insert(0, logMsg);
                if (ActivityLogs.Count > 1000) ActivityLogs.RemoveAt(ActivityLogs.Count - 1);

                // Stats dans la barre centrale uniquement
                if (msg.StartsWith("Durée:") || msg.StartsWith("Stats:"))
                {
                    CurrentRecordingStats = msg.StartsWith("Stats:") ? msg : $"Stats: {msg}";
                    IsRecording = true;
                }
                else if (msg.Contains("Stream détecté") || msg.Contains("Stream en direct détecté"))
                {
                    CurrentAction = msg;
                    IsRecording = true;
                }
                else if (msg.Contains("Le stream est terminé") || msg.Contains("Retour en mode surveillance"))
                {
                    IsRecording = false;
                    CurrentRecordingStats = string.Empty;
                }
            }

            if (System.Windows.Application.Current?.Dispatcher?.CheckAccess() == true)
            {
                AddLogInternal(message);
            }
            else
            {
                System.Windows.Application.Current?.Dispatcher?.Invoke(() => AddLogInternal(message));
            }
        }

        public void SetStats(string message)
        {
            if (System.Windows.Application.Current?.Dispatcher?.CheckAccess() == true)
            {
                CurrentRecordingStats = message.StartsWith("Stats:") ? message : $"Stats: {message}";
                IsRecording = true;
            }
            else
            {
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    CurrentRecordingStats = message.StartsWith("Stats:") ? message : $"Stats: {message}";
                    IsRecording = true;
                });
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
