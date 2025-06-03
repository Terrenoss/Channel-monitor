using System;
using System.IO;
using System.Text;
using System.Windows;

namespace AutoStreamRec.ViewModels
{
    public class LogService
    {
        public void AddLog(string message)
        {
            string timestamp = $"[{DateTime.Now:HH:mm:ss}] {message}";

            string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            Directory.CreateDirectory(logDir);
            string logFile = Path.Combine(logDir, $"log_{DateTime.Now:yyyyMMdd}.txt");
            File.AppendAllText(logFile, timestamp + Environment.NewLine);
        }

        public void CopyLogsToClipboard(MainViewModel vm)
        {
            try
            {
                var sb = new StringBuilder();
                foreach (var log in vm.ActivityLogs)
                    sb.AppendLine(log);

                if (sb.Length > 0)
                {
                    Clipboard.SetText(sb.ToString());
                    AddLog("Les logs ont été copiés dans le presse-papier");
                    vm.StatusMessage = "Logs copiés !";
                }
                else
                {
                    vm.StatusMessage = "Aucun log à copier";
                }
            }
            catch (Exception ex)
            {
                AddLog($"Erreur lors de la copie des logs: {ex.Message}");
                vm.StatusMessage = "Erreur copie logs";
            }
        }

        public void ClearLogs(MainViewModel vm)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                vm.ActivityLogs.Clear();
                vm.CurrentRecordingStats = "Aucun enregistrement en cours";
                AddLog("Journal des logs effacé");
                vm.StatusMessage = "Logs effacés";
            });
        }
    }
}