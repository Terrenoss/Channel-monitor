using AutoStreamRec.Services;
using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

namespace AutoStreamRec.ViewModels
{
    public class MainViewModel : ObservableObject
    {
        private string _youtubeUrl;
        private string _statusMessage = "Prêt";
        private bool _isWorking;
        private bool _isMonitoring;
        private string _currentAction;
        private string _currentRecordingStats = "Aucun enregistrement en cours";
        private ObservableCollection<string> _activityLogs = new();

        private readonly StreamRecorderService _streamRecorder;
        private readonly StreamMonitor _streamMonitor;
        private readonly LogService _logger;
        private readonly ConvertCommandHandler _converter;

        public string YoutubeUrl
        {
            get => _youtubeUrl;
            set => SetProperty(() => _youtubeUrl, v => _youtubeUrl = v, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(() => _statusMessage, v => _statusMessage = v, value);
        }

        public bool IsWorking
        {
            get => _isWorking;
            set => SetProperty(() => _isWorking, v => _isWorking = v, value);
        }

        public bool IsMonitoring
        {
            get => _isMonitoring;
            set => SetProperty(() => _isMonitoring, v => _isMonitoring = v, value);
        }

        public string CurrentAction
        {
            get => _currentAction;
            set => SetProperty(() => _currentAction, v => _currentAction = v, value);
        }

        public string CurrentRecordingStats
        {
            get => _currentRecordingStats;
            set => SetProperty(() => _currentRecordingStats, v => _currentRecordingStats = v, value);
        }

        public ObservableCollection<string> ActivityLogs
        {
            get => _activityLogs;
            set => SetProperty(() => _activityLogs, v => _activityLogs = v, value);
        }

        public ICommand ConvertCommand => new RelayCommand(async () => await _converter.ConvertTsToMp4(this));
        public ICommand MonitorCommand => new RelayCommand(async () => await _streamMonitor.MonitorChannel(this));
        public ICommand StopMonitoringCommand => new RelayCommand(() => _streamMonitor.StopMonitoring(this));
        public ICommand CopyLogsCommand => new RelayCommand(() => _logger.CopyLogsToClipboard(this));
        public ICommand ClearLogsCommand => new RelayCommand(() => _logger.ClearLogs(this));

        public MainViewModel()
        {
            _logger = new LogService();
            _streamRecorder = new StreamRecorderService(_logger.AddLog, UpdateRecordingStats);
            _converter = new ConvertCommandHandler(_streamRecorder);
            _streamMonitor = new StreamMonitor(_streamRecorder);
        }

        private void UpdateRecordingStats(string message)
        {
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                CurrentRecordingStats = $"En cours: {message}";
            });
        }
    }
}
