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
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // Register Services
        builder.Services.AddSingleton<GoogleDriveService>();

        // Register ViewModels
        builder.Services.AddTransient<GoogleDriveViewModel>();

        // Register Views
        builder.Services.AddTransient<GoogleDriveView>();

#if DEBUG
    	builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
