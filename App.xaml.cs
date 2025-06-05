using System.Configuration;
using System.Data;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AutoStreamRec.Services;
using AutoStreamRec.ViewModels;
using System.Threading.Tasks;

namespace AutoStreamRec;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private ServiceProvider _serviceProvider;
    private MainViewModel? _mainViewModel;

    public App()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();
    }

    private void ConfigureServices(ServiceCollection services)
    {
        services.AddLogging(configure => configure.AddConsole());
        
        // Enregistrer les actions de logging
        services.AddSingleton<ILogAction>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<StreamRecorderService>>();
            return new LogAction(message => 
            {
                logger.LogInformation(message);
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (_mainViewModel != null)
                    {
                        _mainViewModel.AddLog(message);
                    }
                });
            });
        });

        services.AddSingleton<IStatsAction>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<StreamRecorderService>>();
            return new StatsAction(message => 
            {
                logger.LogInformation($"Stats: {message}");
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (_mainViewModel != null)
                    {
                        _mainViewModel.SetStats(message);
                    }
                });
            });
        });

        services.AddSingleton<StreamRecorderService>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var mainWindow = _serviceProvider.GetService<MainWindow>();
        _mainViewModel = _serviceProvider.GetService<MainViewModel>();
        mainWindow.Show();
    }
}

public interface ILogAction
{
    void Log(string message);
}

public interface IStatsAction
{
    void Log(string message);
}

public class LogAction : ILogAction
{
    private readonly Action<string> _logAction;

    public LogAction(Action<string> logAction)
    {
        _logAction = logAction;
    }

    public void Log(string message) => _logAction(message);
}

public class StatsAction : IStatsAction
{
    private readonly Action<string> _statsAction;

    public StatsAction(Action<string> statsAction)
    {
        _statsAction = statsAction;
    }

    public void Log(string message) => _statsAction(message);
}
