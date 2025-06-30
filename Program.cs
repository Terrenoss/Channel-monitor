using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.ReactiveUI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Strivea.Services;
using Strivea.Views;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Strivea;

class Program
{
    public static void Main(string[] args)
    {
        try
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
        }
        catch (IOException)
        {
            // Ignore l'erreur si la console n'est pas disponible (ex: Rider, VS, service Windows)
        }
        try
        {
            // Créer le répertoire des logs s'il n'existe pas
            Directory.CreateDirectory("logs");

            // Configurer la configuration
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .Build();

            // Configurer Serilog
            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration)
                .WriteTo.File("logs/startup.log", rollingInterval: RollingInterval.Day)
                .WriteTo.Console()
                .CreateLogger();

            Log.Information("Démarrage de l'application...");

            // Créer l'application
            var app = BuildAvaloniaApp();
            Log.Information("Application Avalonia construite");

            // Configurer les services
            ConfigureServices();

            // Démarrer l'application
            app.StartWithClassicDesktopLifetime(args);
            Log.Information("Application démarrée avec succès");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Erreur fatale : {Message}", ex.Message);
            Console.WriteLine($"Erreur fatale : {ex}");
            Thread.Sleep(5000); // Attendre 5 secondes pour voir l'erreur
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        Log.Information("Initialisation de l'application...");
        var app = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .UseReactiveUI();
        Log.Information("Initialisation du framework terminée...");
        return app;
    }

    private static void ConfigureServices()
    {
        Log.Information("Configuration des services...");
        var services = new ServiceCollection();

        // Enregistrer les services
        services.AddSingleton<IExecutableLocator, ExecutableLocator>();
        services.AddSingleton<IStreamDetector, YouTubeStreamDetector>();
        services.AddSingleton<BaseStreamDetector, YouTubeStreamDetector>();
        services.AddSingleton<IStreamRecorder, StreamRecorder>();
        services.AddSingleton<RecordingStatsLogger>();

        // Construire le conteneur DI
        var serviceProvider = services.BuildServiceProvider();
        Log.Information("Construction du conteneur DI...");
    }
}