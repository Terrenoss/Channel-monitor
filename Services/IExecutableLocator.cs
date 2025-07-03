using System;

namespace Strivea.Services
{
    public interface IExecutableLocator : IDisposable
    {
        string FindExecutable(string executableName);
        bool IsExecutableInstalled(string executableName);
    }
} 