using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;
using Yeetmedia3.Services;
using Yeetmedia3.ViewModels;
using Yeetmedia3.Views;

namespace Yeetmedia3;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkitMediaElement()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // Register Services
        builder.Services.AddSingleton<WebViewService>();
        builder.Services.AddSingleton<GoogleDriveService>();
        builder.Services.AddSingleton<DotnetRocksService>();
        builder.Services.AddSingleton<LoggingService>();

        // Register ViewModels as Singleton to maintain state
        builder.Services.AddSingleton<GoogleDriveViewModel>();
        builder.Services.AddSingleton<DotnetRocksViewModel>();
        builder.Services.AddSingleton<LogViewModel>();

        // Register Views as Singleton to prevent recreation
        builder.Services.AddSingleton<GoogleDriveView>();
        builder.Services.AddSingleton<DotnetRocksView>();
        builder.Services.AddSingleton<LogView>();

#if DEBUG
    	builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
