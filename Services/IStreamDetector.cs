using System.Threading.Tasks;
using Strivea.Models;

namespace Strivea.Services
{
    public interface IStreamDetector
    {
        bool CanHandle(string url);
        Task<bool> IsLiveAsync(string url);
        Task<string> GetStreamUrlAsync(string url);
        Task<StreamInfo> GetStreamInfoAsync(string url);
    }
} 