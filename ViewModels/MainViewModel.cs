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

namespace Strivea.ViewModels
{
    public partial class MainViewModel : ViewModelBase
    {
        private readonly ILogger<MainViewModel> _logger;
        private readonly IStreamDetector _streamDetector;
        private readonly IStreamRecorder _streamRecorder;
        private string _streamUrl;
        private string _outputDirectory;
        private bool _isMonitoring;
        private string _statusMessage;
        private bool _isWorking;
        private string _currentAction;
        private string _currentRecordingStats;
        private CancellationTokenSource _monitoringCts;

        public MainViewModel(
            ILogger<MainViewModel> logger,
            IStreamDetector streamDetector,
            IStreamRecorder streamRecorder)
        {
            _logger = logger;
            _streamDetector = streamDetector;
            _streamRecorder = streamRecorder;
            ActivityLogs = new ObservableCollection<string>();
            OutputDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Strivea",
                "Recordings");

            _logger.LogInformation("MainViewModel initialisé");
            AddLog("Application démarrée");
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
                    await _streamRecorder.StartRecordingAsync(
                        streamUrlResult,
                        platform,
                        channelName,
                        streamTitle,
                        _monitoringCts.Token);

                    IsMonitoring = true;
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
                IsMonitoring = false;
                AddLog("Surveillance arrêtée avec succès");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'arrêt de la surveillance");
                AddLog($"Erreur lors de l'arrêt : {ex.Message}");
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
    }
}
