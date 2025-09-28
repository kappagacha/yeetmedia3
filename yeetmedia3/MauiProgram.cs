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

        // Register ViewModels
        builder.Services.AddTransient<GoogleDriveViewModel>();
        builder.Services.AddTransient<DotnetRocksViewModel>();

        // Register Views
        builder.Services.AddTransient<GoogleDriveView>();
        builder.Services.AddTransient<DotnetRocksView>();

#if DEBUG
    	builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
