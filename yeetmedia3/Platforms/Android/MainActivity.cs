using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Provider;

namespace Yeetmedia3;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTask,
    ResizeableActivity = true,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Request to ignore battery optimizations for better media playback background support
        RequestIgnoreBatteryOptimizations();
    }

    private void RequestIgnoreBatteryOptimizations()
    {
        try
        {
            var pm = (PowerManager?)GetSystemService(PowerService);
            if (pm != null && !pm.IsIgnoringBatteryOptimizations(PackageName))
            {
                var intent = new Intent();
                intent.SetAction(Settings.ActionRequestIgnoreBatteryOptimizations);
                intent.SetData(Android.Net.Uri.Parse("package:" + PackageName));
                StartActivity(intent);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainActivity] Error requesting battery optimization exemption: {ex.Message}");
        }
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        // Handle returning from battery optimization settings
    }
}
