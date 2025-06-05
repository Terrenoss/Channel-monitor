using System;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AutoStreamRec.Models;

namespace AutoStreamRec.Services
{
    public abstract class BaseStreamDetector : IStreamDetector
    {
        protected readonly Action<string> _log;
        protected readonly ExecutableLocator _locator;
        protected const int MAX_RETRIES = 3;
        protected const int RETRY_DELAY_MS = 1000;

        protected BaseStreamDetector(Action<string> logAction, ExecutableLocator locator)
        {
            _log = logAction;
            _locator = locator;
        }

        public abstract bool CanHandle(string url);

        public abstract Task<StreamInfo> DetectStream(string url);

        protected StreamInfo CreateErrorInfo(string errorMessage)
        {
            _log(errorMessage);
            return new StreamInfo
            {
                IsLive = false,
                ErrorMessage = errorMessage,
                DetectionTime = DateTime.Now
            };
        }

        protected abstract string GetPlatformName();
    }
} 