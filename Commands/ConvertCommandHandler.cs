using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Strivea.Services;
using Strivea.ViewModels;

namespace Strivea.Commands
{
    public class ConvertCommandHandler
    {
        private readonly ILogger<ConvertCommandHandler> _logger;
        private readonly VideoConverter _videoConverter;
        private readonly MainViewModel _viewModel;

        public ConvertCommandHandler(
            ILogger<ConvertCommandHandler> logger,
            VideoConverter videoConverter,
            MainViewModel viewModel)
        {
            _logger = logger;
            _videoConverter = videoConverter;
            _viewModel = viewModel;
        }

        public async Task Execute()
        {
            try
            {
                _viewModel.StatusMessage = "Conversion des enregistrements...";
                _viewModel.CurrentAction = "Conversion en cours";

                var result = await _videoConverter.ConvertVideoAsync("input.ts", "output.mp4");
                if (result)
                {
                    _viewModel.StatusMessage = "Conversion terminée avec succès";
                    _logger.LogInformation("Conversion terminée avec succès");
                }
                else
                {
                    _viewModel.StatusMessage = "Erreur lors de la conversion";
                    _logger.LogError("Erreur lors de la conversion");
                }
            }
            catch (Exception ex)
            {
                _viewModel.StatusMessage = "Erreur lors de la conversion";
                _logger.LogError(ex, "Erreur lors de la conversion");
            }
            finally
            {
                _viewModel.CurrentAction = "Conversion terminée";
            }
        }
    }
} 