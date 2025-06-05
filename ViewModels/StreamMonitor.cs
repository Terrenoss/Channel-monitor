using AutoStreamRec.Services;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AutoStreamRec.ViewModels
{
    public class StreamMonitor
    {
        private CancellationTokenSource _monitoringCts;
        private readonly StreamRecorderService _recorder;

        public StreamMonitor(StreamRecorderService recorder)
        {
            _recorder = recorder;
        }

        public async Task MonitorChannel(MainViewModel vm)
        {
            if (vm.IsMonitoring) return;

            vm.IsMonitoring = true;
            vm.IsWorking = true;
            vm.CurrentAction = "Surveillance en cours";
            vm.StatusMessage = "Démarrage surveillance...";
            vm.ActivityLogs.Insert(0, "[Monitor] Initialisation");

            string baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "recordings");
            if (!ViewModelHelpers.CheckWriteAccess(baseDir, vm)) return;

            if (!await _recorder.CheckDependencies())
            {
                vm.StatusMessage = "Dépendances manquantes";
                return;
            }

            if (string.IsNullOrWhiteSpace(vm.StreamUrl))
            {
                vm.StatusMessage = "URL requise";
                return;
            }

            _monitoringCts = new CancellationTokenSource();
            vm.ActivityLogs.Insert(0, $"[Monitor] Surveillance de {vm.StreamUrl}");

            try
            {
                while (!_monitoringCts.Token.IsCancellationRequested)
                {
                    vm.ActivityLogs.Insert(0, "[Monitor] Vérification du live...");

                    var streamInfo = await _recorder.GetLiveStreamInfo(vm.StreamUrl);

                    if (streamInfo.IsLive)
                    {
                        vm.ActivityLogs.Insert(0, $"[Monitor] Stream détecté sur {streamInfo.Platform} ({streamInfo.Quality})");

                        try
                        {
                            bool success = await _recorder.RecordStream(vm.StreamUrl, _monitoringCts.Token);
                            vm.ActivityLogs.Insert(0, success
                                ? "[Monitor] Enregistrement terminé avec succès"
                                : "[Monitor] Erreur d'enregistrement");
                        }
                        catch (OperationCanceledException)
                        {
                            vm.ActivityLogs.Insert(0, "[Monitor] Enregistrement annulé manuellement");
                        }
                    }
                    else
                    {
                        vm.ActivityLogs.Insert(0, "[Monitor] Aucun stream actif");
                    }

                    await Task.Delay(TimeSpan.FromSeconds(30), _monitoringCts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                vm.ActivityLogs.Insert(0, "[Monitor] Surveillance arrêtée");
                vm.StatusMessage = "Surveillance arrêtée";
            }
            catch (Exception ex)
            {
                vm.ActivityLogs.Insert(0, $"[Monitor] ERREUR: {ex.Message}");
            }
            finally
            {
                _monitoringCts?.Dispose();
                vm.IsMonitoring = false;
                vm.IsWorking = false;
                vm.CurrentAction = "";
                vm.StatusMessage = "Prêt";
                vm.CurrentRecordingStats = "Aucun enregistrement en cours";
            }
        }

        public void StopMonitoring(MainViewModel vm)
        {
            if (!vm.IsMonitoring) return;

            vm.ActivityLogs.Insert(0, "[Monitor] Arrêt demandé");
            _monitoringCts?.Cancel();
            _recorder.StopRecording();
            vm.StatusMessage = "Arrêt en cours...";
            vm.CurrentRecordingStats = "Arrêt demandé...";
        }
    }
}
