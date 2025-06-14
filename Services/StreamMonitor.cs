using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Strivea.Models;
using Strivea.ViewModels;

namespace Strivea.Services
{
    public class StreamMonitor
    {
        private readonly ILogger<StreamMonitor> _logger;
        private readonly StreamManager _streamManager;
        private readonly MainViewModel _viewModel;
        private CancellationTokenSource _cancellationTokenSource;

        public StreamMonitor(
            ILogger<StreamMonitor> logger,
            StreamManager streamManager,
            MainViewModel viewModel)
        {
            _logger = logger;
            _streamManager = streamManager;
            _viewModel = viewModel;
        }

        public async Task StartMonitoring(string url)
        {
            if (_cancellationTokenSource != null)
            {
                _logger.LogWarning("La surveillance est déjà en cours");
                return;
            }

            _cancellationTokenSource = new CancellationTokenSource();
            _viewModel.CurrentAction = "Surveillance en cours";

            try
            {
                while (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    var streamInfo = await _streamManager.GetStreamInfo(url);
                    
                    if (streamInfo.IsLive)
                    {
                        _logger.LogInformation($"Stream en direct détecté : {url}");
                        await _streamManager.StartRecording(url, streamInfo.Platform);
                    }
                    else
                    {
                        _logger.LogInformation($"Stream hors ligne : {url}");
                    }

                    await Task.Delay(TimeSpan.FromMinutes(1), _cancellationTokenSource.Token);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Surveillance arrêtée");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la surveillance du stream");
                _viewModel.StatusMessage = "Erreur lors de la surveillance du stream";
            }
            finally
            {
                _cancellationTokenSource = null;
                _viewModel.CurrentAction = "Surveillance arrêtée";
            }
        }

        public void StopMonitoring()
        {
            if (_cancellationTokenSource == null)
            {
                _logger.LogWarning("Aucune surveillance en cours");
                return;
            }

            _cancellationTokenSource.Cancel();
            _viewModel.CurrentAction = "Arrêt de la surveillance";
        }
    }
} 