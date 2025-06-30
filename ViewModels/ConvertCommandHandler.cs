using Strivea.Services;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Strivea.ViewModels
{
    public class ConvertCommandHandler
    {
        private readonly StreamRecorderService _recorder;

        public ConvertCommandHandler(StreamRecorderService recorder)
        {
            _recorder = recorder;
        }

        public async Task ConvertTsToMp4(MainViewModel vm)
        {
            try
            {
                vm.IsWorking = true;
                vm.CurrentAction = "Conversion TS → MP4";
                vm.ActivityLogs.Insert(0, "[Convert] Début de la conversion");

                if (!await _recorder.CheckDependencies())
                {
                    vm.StatusMessage = "Dépendances manquantes";
                    return;
                }

                string recordingsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "recordings");
                string tsFile = Path.Combine(recordingsDir, "temp.ts");

                if (!File.Exists(tsFile))
                {
                    vm.StatusMessage = "Fichier TS introuvable";
                    vm.ActivityLogs.Insert(0, "[Convert] Aucun fichier à convertir");
                    return;
                }

                if (await _recorder.ConvertToMp4(tsFile))
                {
                    vm.StatusMessage = "Conversion réussie";
                    vm.ActivityLogs.Insert(0, "[Convert] Conversion terminée");
                }
                else
                {
                    vm.StatusMessage = "Erreur de conversion";
                    vm.ActivityLogs.Insert(0, "[Convert] Erreur pendant la conversion");
                }
            }
            catch (Exception ex)
            {
                vm.ActivityLogs.Insert(0, $"[Convert] Erreur: {ex.Message}");
                vm.StatusMessage = "Erreur conversion";
            }
            finally
            {
                vm.IsWorking = false;
                vm.CurrentAction = "";
            }
        }
    }
}
