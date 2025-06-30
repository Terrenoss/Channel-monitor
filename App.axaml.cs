using System;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Strivea.Services;
using Strivea.ViewModels;
using Strivea.Views;
using System.Threading.Tasks;
using System.Diagnostics;

namespace Strivea;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private static IServiceProvider _serviceProvider;

    public static IServiceProvider ServiceProvider => _serviceProvider;

    public override void Initialize()
    {
        Log.Information("Démarrage de l'application...");
        File.AppendAllText("startup.log", "Initialisation de l'application...\n");
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        try
        {
            Log.Information("Initialisation du framework terminée...");
            File.AppendAllText("startup.log", "Initialisation du framework terminée...\n");

            var services = new ServiceCollection();
            
            // Configurer les services
            services.AddLogging(builder =>
            {
                builder.AddSerilog(dispose: true);
            });
            
            // Enregistrer les services
            services.AddSingleton<IExecutableLocator, ExecutableLocator>();
            services.AddSingleton<Microsoft.Extensions.Logging.ILogger<YouTubeStreamDetector>>(sp => 
                sp.GetRequiredService<ILoggerFactory>().CreateLogger<YouTubeStreamDetector>());
            services.AddSingleton<Microsoft.Extensions.Logging.ILogger>(sp => 
                sp.GetRequiredService<ILoggerFactory>().CreateLogger<YouTubeStreamDetector>());
            services.AddSingleton<IStreamDetector, YouTubeStreamDetector>();
            services.AddSingleton<BaseStreamDetector, YouTubeStreamDetector>();
            services.AddSingleton<IStreamRecorder, StreamRecorder>();
            services.AddSingleton<RecordingStatsLogger>();
            services.AddSingleton<MainViewModel>(sp =>
                new MainViewModel(
                    sp.GetRequiredService<ILogger<MainViewModel>>(),
                    sp.GetRequiredService<IStreamDetector>(),
                    sp
                )
            );

            Debug.WriteLine("Construction du conteneur DI...");
            File.AppendAllText("startup.log", "Construction du conteneur DI...\n");
            _serviceProvider = services.BuildServiceProvider();

            Debug.WriteLine("Création de la fenêtre principale...");
            File.AppendAllText("startup.log", "Création de la fenêtre principale...\n");
            
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow
                {
                    DataContext = _serviceProvider.GetRequiredService<MainViewModel>()
                };
                
                Debug.WriteLine("Fenêtre principale créée");
                File.AppendAllText("startup.log", "Fenêtre principale créée\n");
            }

            Log.Information("Application démarrée avec succès");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Erreur fatale : {Message}", ex.Message);
            File.WriteAllText("error.log", $"Erreur fatale : {ex}");
            throw;
        }

        base.OnFrameworkInitializationCompleted();
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

    public void Log(string message) => _logAction.Invoke(message);
}

public class StatsAction : IStatsAction
{
    private readonly Action<string> _statsAction;

    public StatsAction(Action<string> statsAction)
    {
        _statsAction = statsAction;
    }

    public void Log(string message) => _statsAction.Invoke(message);
}
