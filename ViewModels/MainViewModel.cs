using AutoStreamRec.Services;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
        private CancellationTokenSource _monitoringCts;

        public string YoutubeUrl
        {
            get => _youtubeUrl;
            set => SetProperty(ref _youtubeUrl, value);
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

        public bool IsMonitoring
        {
            get => _isMonitoring;
            set => SetProperty(ref _isMonitoring, value);
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

        public ObservableCollection<string> ActivityLogs
        {
            get => _activityLogs;
            set => SetProperty(ref _activityLogs, value);
        }

        public ICommand ConvertCommand => new RelayCommand(async () => await ConvertTsToMp4());
        public ICommand MonitorCommand => new RelayCommand(async () => await MonitorChannel());
        public ICommand StopMonitoringCommand => new RelayCommand(() => StopMonitoring());
        public ICommand CopyLogsCommand => new RelayCommand(CopyLogsToClipboard);
        public ICommand ClearLogsCommand => new RelayCommand(ClearLogs);

        public MainViewModel()
        {
            _streamRecorder = new StreamRecorderService(AddLog, UpdateRecordingStats);
        }

        private void UpdateRecordingStats(string message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                CurrentRecordingStats = $"En cours: {message}";
            });
        }

        private void CopyLogsToClipboard()
        {
            try
            {
                var sb = new StringBuilder();
                foreach (var log in ActivityLogs)
                {
                    sb.AppendLine(log);
                }

                if (sb.Length > 0)
                {
                    Clipboard.SetText(sb.ToString());
                    AddLog("Les logs ont été copiés dans le presse-papier");
                    StatusMessage = "Logs copiés !";
                }
                else
                {
                    AddLog("Aucun log à copier");
                    StatusMessage = "Aucun log à copier";
                }
            }
            catch (Exception ex)
            {
                AddLog($"Erreur lors de la copie des logs: {ex.Message}");
                StatusMessage = "Erreur copie logs";
            }
        }

        private void ClearLogs()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                ActivityLogs.Clear();
                CurrentRecordingStats = "Aucun enregistrement en cours";
                AddLog("Journal des logs effacé");
                StatusMessage = "Logs effacés";
            });
        }

        private void AddLog(string message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                ActivityLogs.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {message}");
                if (ActivityLogs.Count > 200)
                    ActivityLogs.RemoveAt(ActivityLogs.Count - 1);
            });

            string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            Directory.CreateDirectory(logDir);
            string logFile = Path.Combine(logDir, $"log_{DateTime.Now:yyyyMMdd}.txt");
            File.AppendAllText(logFile, $"[{DateTime.Now:HH:mm:ss}] {message}" + Environment.NewLine);
        }

        private async Task ConvertTsToMp4()
        {
            try
            {
                IsWorking = true;
                CurrentAction = "Conversion TS → MP4";
                AddLog("Début de la conversion");

                if (!await _streamRecorder.CheckDependencies())
                {
                    StatusMessage = "Dépendances manquantes";
                    return;
                }

                string recordingsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "recordings");
                string tsFile = Path.Combine(recordingsDir, "temp.ts");
                
                if (!File.Exists(tsFile))
                {
                    StatusMessage = "Fichier TS introuvable";
                    AddLog("Aucun fichier à convertir");
                    return;
                }

                if (await _streamRecorder.ConvertToMp4(tsFile))
                {
                    StatusMessage = "Conversion réussie";
                    AddLog("Conversion terminée");
                }
                else
                {
                    StatusMessage = "Erreur de conversion";
                }
            }
            catch (Exception ex)
            {
                AddLog($"Erreur conversion: {ex.Message}");
                StatusMessage = "Erreur conversion";
            }
            finally
            {
                IsWorking = false;
                CurrentAction = "";
            }
        }

        private async Task MonitorChannel()
        {
            if (IsMonitoring) return;

            try
            {
                IsMonitoring = true;
                IsWorking = true;
                CurrentAction = "Surveillance en cours";
                StatusMessage = "Démarrage surveillance...";
                AddLog("Initialisation surveillance");

                string baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "recordings");
                if (!CheckWriteAccess(baseDir))
                {
                    StatusMessage = "Erreur permissions";
                    AddLog("ERREUR: Pas de permissions d'écriture dans le dossier d'enregistrement");
                    return;
                }

                if (!await _streamRecorder.CheckDependencies())
                {
                    StatusMessage = "Dépendances manquantes";
                    return;
                }

                if (string.IsNullOrWhiteSpace(YoutubeUrl))
                {
                    StatusMessage = "URL YouTube requise";
                    return;
                }

                _monitoringCts = new CancellationTokenSource();
                AddLog($"Début surveillance: {YoutubeUrl}");

                while (!_monitoringCts.Token.IsCancellationRequested)
                {
                    AddLog("Scan des streams disponibles...");
                    string quality = await _streamRecorder.GetLiveStreamUrl(YoutubeUrl);
                    
                    if (!string.IsNullOrEmpty(quality))
                    {
                        AddLog($"Stream détecté - Lancement enregistrement (qualité: {quality})");
                        bool success = await _streamRecorder.RecordYouTubeStream(YoutubeUrl, _monitoringCts.Token);
                        
                        if (success) 
                        {
                            AddLog("Enregistrement terminé avec succès");
                            StatusMessage = "Enregistrement réussi";
                        }
                        else
                        {
                            AddLog("Problème lors de l'enregistrement");
                            StatusMessage = "Erreur enregistrement";
                        }
                    }
                    else
                    {
                        AddLog($"Aucun stream actif détecté ({DateTime.Now:HH:mm:ss})");
                        StatusMessage = "En attente de stream...";
                    }

                    AddLog("Prochaine vérification dans 30 secondes...");
                    await Task.Delay(TimeSpan.FromSeconds(30), _monitoringCts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                AddLog("Surveillance arrêtée par l'utilisateur");
                StatusMessage = "Surveillance arrêtée";
            }
            catch (Exception ex)
            {
                AddLog($"ERREUR surveillance: {ex.Message}");
                StatusMessage = "Erreur surveillance";
            }
            finally
            {
                _monitoringCts?.Dispose();
                IsMonitoring = false;
                IsWorking = false;
                CurrentAction = "";
                StatusMessage = "Prêt";
                CurrentRecordingStats = "Aucun enregistrement en cours";
            }
        }
        
        private bool CheckWriteAccess(string folderPath)
        {
            try
            {
                string testFile = Path.Combine(folderPath, "write_test.tmp");
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);
                return true;
            }
            catch (Exception ex)
            {
                AddLog($"ERREUR Permission écriture: {ex.Message}");
                return false;
            }
        }

        private void StopMonitoring()
        {
            if (!IsMonitoring) return;

            try
            {
                AddLog("Demande arrêt surveillance...");
                _monitoringCts?.Cancel();
                _streamRecorder.StopRecording();
                StatusMessage = "Arrêt en cours...";
                CurrentRecordingStats = "Arrêt demandé...";
            }
            catch (Exception ex)
            {
                AddLog($"Erreur arrêt: {ex.Message}");
            }
        }
    }
}