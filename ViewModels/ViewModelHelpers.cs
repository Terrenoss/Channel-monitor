using System;
using System.IO;

namespace AutoStreamRec.ViewModels
{
    public static class ViewModelHelpers
    {
        public static bool CheckWriteAccess(string folderPath, MainViewModel vm)
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
                vm.ActivityLogs.Insert(0, $"[Permission] ERREUR écriture: {ex.Message}");
                return false;
            }
        }
    }
}