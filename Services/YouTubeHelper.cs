using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace AutoStreamRec.Services
{
    public class YouTubeHelper
    {
        private readonly ExecutableLocator _locator;

        public YouTubeHelper(ExecutableLocator locator)
        {
            _locator = locator;
        }

        public async Task<string> GetChannelNameAsync(string youtubeUrl)
        {
            if (youtubeUrl.Contains("@"))
            {
                int atIndex = youtubeUrl.IndexOf('@');
                int nextSlash = youtubeUrl.IndexOf('/', atIndex);

                if (nextSlash == -1)
                    return youtubeUrl.Substring(atIndex + 1);

                return youtubeUrl.Substring(atIndex + 1, nextSlash - atIndex - 1);
            }

            string ytDlpPath = _locator.FindExecutablePath("yt-dlp");
            if (!string.IsNullOrEmpty(ytDlpPath))
            {
                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = ytDlpPath,
                        Arguments = $"\"{youtubeUrl}\" --print \"%(channel)s\" --no-warnings",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var process = Process.Start(startInfo);
                    string output = await process.StandardOutput.ReadToEndAsync();
                    await process.WaitForExitAsync();

                    if (!string.IsNullOrWhiteSpace(output))
                        return output.Trim();
                }
                catch { }
            }

            return "Unknown_Channel";
        }
    }
}