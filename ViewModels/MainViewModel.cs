using AutoStreamRec.Services;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace AutoStreamRec.ViewModels
{
    public class MainViewModel : ObservableObject
    {
        private string _youtubeUrl;
        private string _statusMessage;
        private bool _isWorking;
        private string _currentAction;
        private ObservableCollection<string> _activityLogs = new ObservableCollection<string>();

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

        public string CurrentAction
        {
            get => _currentAction;
            set => SetProperty(ref _currentAction, value);
        }

        public ObservableCollection<string> ActivityLogs
        {
            get => _activityLogs;
            set => SetProperty(ref _activityLogs, value);
        }

        public ICommand ConvertCommand => new RelayCommand(async () => await ConvertTsToMp4());
        public ICommand MonitorCommand => new RelayCommand(async () => await MonitorChannel());

        private void AddLog(string message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                ActivityLogs.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {message}");
                if (ActivityLogs.Count > 100) ActivityLogs.RemoveAt(ActivityLogs.Count - 1);
            });
        }

        private async Task<bool> InstallDependencyPrompt(string message)
        {
            return await Application.Current.Dispatcher.Invoke(async () => 
            {
                var result = MessageBox.Show(
                    $"{message}\n\nL'application va tenter d'installer automatiquement la dépendance.",
                    "Dépendance manquante",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    StatusMessage = "Installation en cours...";
                    return true;
                }
                return false;
            });
        }

        private async Task ConvertTsToMp4()
        {
            try
            {
                IsWorking = true;
                CurrentAction = "Conversion TS → MP4";
                
                var baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "recordings");
                var converter = new StreamRecorderService(AddLog, InstallDependencyPrompt);
                
                if (!await converter.CheckDependencies())
                {
                    StatusMessage = "Dépendances requises non satisfaites";
                    return;
                }
                
                var tsFiles = Directory.GetFiles(baseDir, "*.ts", SearchOption.AllDirectories);
                int convertedCount = 0;

                foreach (var tsFile in tsFiles)
                {
                    try
                    {
                        var mp4Dir = Path.Combine(baseDir, "Conversions");
                        Directory.CreateDirectory(mp4Dir);
                        var mp4File = Path.Combine(mp4Dir, $"{Path.GetFileNameWithoutExtension(tsFile)}.mp4");

                        await converter.ConvertToMp4(tsFile, mp4File);
                        convertedCount++;
                    }
                    catch (Exception ex)
                    {
                        AddLog($"ERREUR conversion {Path.GetFileName(tsFile)}: {ex.Message}");
                    }
                }

                StatusMessage = convertedCount > 0 ? $"{convertedCount} fichiers convertis" : "Aucun fichier TS trouvé";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erreur: {ex.Message}";
                AddLog($"ERREUR: {ex.Message}");
            }
            finally
            {
                IsWorking = false;
                CurrentAction = "";
            }
        }

        private async Task MonitorChannel()
        {
            if (string.IsNullOrWhiteSpace(YoutubeUrl))
            {
                StatusMessage = "URL YouTube requise";
                return;
            }

            try
            {
                IsWorking = true;
                CurrentAction = "Surveillance YouTube";
                
                var recorder = new StreamRecorderService(AddLog, InstallDependencyPrompt);
                
                if (!await recorder.CheckDependencies())
                {
                    StatusMessage = "Dépendances requises non satisfaites";
                    return;
                }
                
                StatusMessage = "Recherche d'un stream en cours...";
                await recorder.RecordYouTubeStream(YoutubeUrl);
                StatusMessage = "Enregistrement terminé";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erreur: {ex.Message}";
                AddLog($"ERREUR: {ex.Message}");
                
                if (ex.Message.Contains("confirm your age"))
                {
                    AddLog("NOTE: Ce stream nécessite une vérification d'âge");
                    AddLog("Solution alternative:");
                    AddLog("1. Obtenez l'URL directe du stream");
                    AddLog("2. Utilisez cette URL directement");
                }
                
                await Task.Delay(2000);
                StatusMessage = "Prêt à réessayer";
            }
            finally
            {
                IsWorking = false;
                CurrentAction = "";
            }
        }
    }
}