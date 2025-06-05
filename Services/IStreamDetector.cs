using System.Threading.Tasks;
using AutoStreamRec.Models;

namespace AutoStreamRec.Services
{
    public interface IStreamDetector
    {
        bool CanHandle(string url);
        Task<StreamInfo> DetectStream(string url);
    }
} 